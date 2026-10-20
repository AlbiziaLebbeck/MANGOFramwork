using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class CartTests
{
    internal static async Task<ShopperActor> ShopperAsync(CommerceDb db, string subject)
    {
        var shopper = new Shopper { Issuer = "test", Subject = subject, DisplayName = subject };
        db.Shoppers.Add(shopper); await db.SaveChangesAsync();
        return new(shopper.Id, shopper.Issuer, subject, subject, []);
    }

    [PgFact]
    public async Task Concurrent_cart_edits_reject_one_stale_version()
    {
        await using var database = await PgDatabase.CreateAsync();
        Guid shopperId; Guid variantId;
        await using (var db = database.Open())
        {
            shopperId = (await ShopperAsync(db, "alice")).ShopperId;
            variantId = (await CatalogTests.PublishedAsync(db)).Variant.Id;
        }
        var results = await Task.WhenAll(new[] { 1, 2 }.Select(async quantity =>
        {
            await using var db = database.Open();
            try { await new CartService(db, new(), TimeProvider.System).ChangeAsync(shopperId, variantId, new(quantity, 0), default); return "ok"; }
            catch (CommerceException e) { return e.Code; }
        }));
        Assert.Single(results, x => x == "ok");
        Assert.Single(results, x => x == "cart_changed");
        await using (var db = database.Open())
        {
            var cart = await new CartService(db, new(), TimeProvider.System).GetAsync(shopperId, default);
            Assert.Equal(1, cart.Version); Assert.Single(cart.Lines);
        }
    }

    [PgFact]
    public async Task Mixed_quote_uses_exact_server_prices_and_delivery_and_does_not_reserve_stock()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ShopperAsync(db, "alice");
        var physical = await CatalogTests.PublishedAsync(db, "physical", price: 19978);
        var digital = await CatalogTests.PublishedAsync(db, "digital", ProductKind.Digital, price: 11);
        var policy = new CommerceOptions { ShippingEnabled = true, ShippingFeeMinor = 7000 };
        var cart = new CartService(db, policy, TimeProvider.System);
        await cart.ChangeAsync(actor.ShopperId, physical.Variant.Id, new(3, 0), default);
        await cart.ChangeAsync(actor.ShopperId, digital.Variant.Id, new(2, 1), default);
        var quote = await cart.QuoteAsync(actor, new(2, new("สมชาย", "buyer@example.com", "081-234-5678"),
            new("shipping", new("99 อาคาร A", "ปทุมวัน", "ปทุมวัน", "กรุงเทพมหานคร", "10330"))), default);
        Assert.Equal(59956, quote.SubtotalMinor);
        Assert.Equal(66956, quote.TotalMinor);
        Assert.Equal("0812345678", quote.Buyer.Phone);
        Assert.Equal(0, await db.Reservations.CountAsync());
        Assert.Equal(0, physical.Variant.Reserved);
        Assert.Equal(2, quote.Lines.Length);
        Assert.Equal("THB", quote.Currency);
    }

    [PgFact]
    public async Task Quote_rejects_stale_cart_or_unpublished_item_and_ignores_old_cart_prices()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ShopperAsync(db, "alice");
        var (product, variant) = await CatalogTests.PublishedAsync(db);
        var cart = new CartService(db, new(), TimeProvider.System);
        await cart.ChangeAsync(actor.ShopperId, variant.Id, new(1, 0), default);
        var input = new QuoteInput(1, new("Alice", Phone: "0812345678"), new("pickup"));
        Assert.Equal("cart_changed", (await Assert.ThrowsAsync<CommerceException>(() => cart.QuoteAsync(actor, input with { CartVersion = 0 }, default))).Code);
        var catalog = new CatalogService(db);
        await catalog.WriteVariantAsync(product.Id, variant.Id, new(product.Version, variant.Sku, variant.Name, 77777, true, true, []), "test:admin", default);
        Assert.Equal(77777, (await cart.QuoteAsync(actor, input, default)).TotalMinor);
        await catalog.PublishAsync(product.Id, new(product.Version, false), "test:admin", default);
        Assert.Equal("product_unavailable", (await Assert.ThrowsAsync<CommerceException>(() => cart.QuoteAsync(actor, input, default))).Code);
    }

    [PgFact]
    public async Task Physical_delivery_requires_contact_and_complete_address_but_digital_drops_unused_address()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ShopperAsync(db, "physical-buyer");
        var physical = await CatalogTests.PublishedAsync(db);
        var cart = new CartService(db, new() { ShippingEnabled = true }, TimeProvider.System);
        await cart.ChangeAsync(actor.ShopperId, physical.Variant.Id, new(1, 0), default);
        await Assert.ThrowsAsync<CommerceException>(() => cart.QuoteAsync(actor, new(1, new("Buyer"), new("pickup")), default));
        await Assert.ThrowsAsync<CommerceException>(() => cart.QuoteAsync(actor, new(1, new("Buyer", Phone: "0812345678"), new("shipping", new("Address", "", "District", "Province", "x"))), default));
        var digitalBuyer = await ShopperAsync(db, "digital-buyer");
        var digital = await CatalogTests.PublishedAsync(db, "digital-item", ProductKind.Digital);
        await cart.ChangeAsync(digitalBuyer.ShopperId, digital.Variant.Id, new(1, 0), default);
        var quote = await cart.QuoteAsync(digitalBuyer, new(1, new("Buyer"), new("shipping", new("Sensitive", "", "", "", ""))), default);
        Assert.Equal("digital", quote.Delivery.Method); Assert.Null(quote.Delivery.Address);
    }

    [PgFact]
    public async Task Http_cart_is_scoped_to_session_and_client_amount_is_ignored()
    {
        await using var database = await PgDatabase.CreateAsync();
        Guid variantId;
        await using (var db = database.Open()) variantId = (await CatalogTests.PublishedAsync(db)).Variant.Id;
        await using var factory = new CommerceApiFactory(database.ConnectionString);
        using var alice = factory.ShopperClient("alice"); using var bob = factory.ShopperClient("bob");
        await SignInAsync(alice); await SignInAsync(bob);
        var changed = await alice.PutAsJsonAsync($"/ttb-commerce/v1/cart/lines/{variantId}", new { quantity = 1, expectedVersion = 0, unitPriceMinor = 1, shopperId = "bob" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var aliceCart = await alice.GetFromJsonAsync<JsonElement>("/ttb-commerce/v1/cart");
        var bobCart = await bob.GetFromJsonAsync<JsonElement>("/ttb-commerce/v1/cart");
        Assert.Equal(19978, aliceCart.GetProperty("subtotalMinor").GetInt64());
        Assert.Empty(bobCart.GetProperty("lines").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await bob.PostAsJsonAsync("/ttb-commerce/v1/checkout/quote", new { cartVersion = 0, buyer = new { name = "Bob" }, delivery = new { method = "digital" } })).StatusCode);
    }

    internal static async Task<JsonElement> SignInAsync(HttpClient client)
    {
        var result = await client.PostAsync("/ttb-commerce/v1/session", null); result.EnsureSuccessStatusCode();
        var json = await result.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-CSRF-Token", json.GetProperty("csrfToken").GetString());
        return json;
    }
}
