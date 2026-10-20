using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class OrderTests
{
    internal static OrderService Service(CommerceDb db, CommerceOptions? options = null, TimeProvider? clock = null)
    {
        options ??= new(); clock ??= TimeProvider.System;
        return new(db, new CartService(db, options, clock), new Numbering(db, clock), options, clock, new BankReadiness(options, new()));
    }

    internal static async Task<QuoteView> QuoteAsync(CommerceDb db, ShopperActor actor, params (Guid VariantId, int Quantity)[] lines)
    {
        var carts = new CartService(db, new(), TimeProvider.System);
        var version = (await carts.GetAsync(actor.ShopperId, default)).Version;
        foreach (var line in lines) version = (await carts.ChangeAsync(actor.ShopperId, line.VariantId, new(line.Quantity, version), default)).Version;
        return await carts.QuoteAsync(actor, new(version, new(actor.DisplayName, Phone: "0812345678"), new("pickup")), default);
    }

    [PgFact]
    public async Task Duplicate_checkout_creates_one_order_reservation_and_expiry_job()
    {
        await using var database = await PgDatabase.CreateAsync();
        ShopperActor actor; QuoteView quote;
        await using (var db = database.Open())
        {
            actor = await CartTests.ShopperAsync(db, "alice");
            var product = await CatalogTests.PublishedAsync(db);
            quote = await QuoteAsync(db, actor, (product.Variant.Id, 2));
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = database.Open();
            return await Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "same-checkout", default);
        }));
        Assert.Single(results.Select(x => x.Id).Distinct());
        await using (var db = database.Open())
        {
            Assert.Equal(1, await db.Orders.CountAsync()); Assert.Equal(1, await db.Reservations.CountAsync()); Assert.Equal(1, await db.Jobs.CountAsync());
            Assert.Equal(2, (await db.Variants.SingleAsync()).Reserved);
            Assert.Empty((await new CartService(db, new(), TimeProvider.System).GetAsync(actor.ShopperId, default)).Lines);
            Assert.Equal(39956, results[0].TotalMinor);
        }
    }

    [PgFact]
    public async Task Two_buyers_cannot_reserve_the_last_unit_twice()
    {
        await using var database = await PgDatabase.CreateAsync();
        var buyers = new List<(Guid ShopperId, Guid QuoteId)>();
        await using (var db = database.Open())
        {
            var product = await CatalogTests.PublishedAsync(db, stock: 1);
            foreach (var name in new[] { "alice", "bob" })
            {
                var actor = await CartTests.ShopperAsync(db, name);
                buyers.Add((actor.ShopperId, (await QuoteAsync(db, actor, (product.Variant.Id, 1))).Id));
            }
        }
        var results = await Task.WhenAll(buyers.Select(async buyer =>
        {
            await using var db = database.Open();
            try { await Service(db).CreateAsync(buyer.ShopperId, new(buyer.QuoteId), "buy-last-unit", default); return "ok"; }
            catch (CommerceException e) { return e.Code; }
        }));
        Assert.Single(results, x => x == "ok"); Assert.Single(results, x => x == "insufficient_stock");
        await using (var db = database.Open())
        {
            Assert.Equal(1, await db.Orders.CountAsync()); Assert.Equal(1, await db.Reservations.CountAsync());
            Assert.Equal(1, (await db.Variants.SingleAsync()).Reserved);
            Assert.Equal(1, await db.CartLines.CountAsync()); // losing buyer's cart survives the rollback
        }
    }

    [PgFact]
    public async Task Same_checkout_key_with_another_quote_is_a_conflict()
    {
        await using var database = await PgDatabase.CreateAsync();
        Guid shopperId; Guid first; Guid second;
        await using (var db = database.Open())
        {
            var actor = await CartTests.ShopperAsync(db, "alice"); shopperId = actor.ShopperId;
            var variant = (await CatalogTests.PublishedAsync(db)).Variant;
            first = (await QuoteAsync(db, actor, (variant.Id, 1))).Id;
            second = (await new CartService(db, new(), TimeProvider.System).QuoteAsync(actor, new(1, new("Alice", Phone: "0812345678"), new("pickup")), default)).Id;
        }
        await using (var db = database.Open()) await Service(db).CreateAsync(shopperId, new(first), "key", default);
        await using (var db = database.Open())
            Assert.Equal("idempotency_conflict", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).CreateAsync(shopperId, new(second), "key", default))).Code);
    }

    [PgFact]
    public async Task Price_change_expiry_and_snapshot_corruption_cannot_create_an_order()
    {
        await using var database = await PgDatabase.CreateAsync();
        ShopperActor actor; QuoteView quote; Product product; ProductVariant variant;
        await using (var db = database.Open())
        {
            actor = await CartTests.ShopperAsync(db, "alice"); (product, variant) = await CatalogTests.PublishedAsync(db);
            quote = await QuoteAsync(db, actor, (variant.Id, 1));
        }
        var later = new MutableClock(quote.ExpiresAt.AddSeconds(1));
        await using (var db = database.Open())
            Assert.Equal("quote_expired", (await Assert.ThrowsAsync<CommerceException>(() => Service(db, clock: later).CreateAsync(actor.ShopperId, new(quote.Id), "expired", default))).Code);
        await using (var db = database.Open())
            await new CatalogService(db).WriteVariantAsync(product.Id, variant.Id, new(product.Version, variant.Sku, variant.Name, 99999, true, true, []), "test:admin", default);
        await using (var db = database.Open())
            Assert.Equal("quote_changed", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "stale", default))).Code);
        await using (var db = database.Open())
            await db.Quotes.Where(x => x.Id == quote.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SnapshotHash, "invalid"));
        await using (var db = database.Open())
            Assert.Equal("quote_integrity", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "corrupt", default))).Code);
        await using (var db = database.Open()) { Assert.Equal(0, await db.Orders.CountAsync()); Assert.Equal(0, await db.Reservations.CountAsync()); }
    }

    [PgFact]
    public async Task Mixed_order_is_immutable_and_expiry_releases_stock_only_once()
    {
        await using var database = await PgDatabase.CreateAsync();
        ShopperActor actor; OrderView order; Guid physicalId;
        await using (var db = database.Open())
        {
            actor = await CartTests.ShopperAsync(db, "alice");
            var physical = await CatalogTests.PublishedAsync(db); physicalId = physical.Variant.Id;
            var digital = await CatalogTests.PublishedAsync(db, "digital", ProductKind.Digital);
            var quote = await QuoteAsync(db, actor, (physicalId, 2), (digital.Variant.Id, 3));
            order = await Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "mixed", default);
        }
        await using (var db = database.Open())
        {
            await db.Products.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Renamed after order"));
            var current = await Service(db).GetAsync(actor.ShopperId, order.Id, default);
            Assert.All(current.Lines, line => Assert.Equal("โคมไฟ MANGO", line.Name));
            Assert.Equal(99890, current.TotalMinor);
        }
        var clock = new MutableClock(order.ExpiresAt.AddSeconds(1));
        await using (var db = database.Open()) await Service(db, clock: clock).ExpireAsync(order.Id, default);
        await using (var db = database.Open()) await Service(db, clock: clock).ExpireAsync(order.Id, default);
        await using (var db = database.Open())
        {
            Assert.Equal(0, (await db.Variants.SingleAsync(x => x.Id == physicalId)).Reserved);
            Assert.Equal(1, await db.StockMovements.CountAsync(x => x.EffectKey.StartsWith("release:")));
            Assert.Equal(ReservationState.Released, (await db.Reservations.SingleAsync()).State);
            Assert.Equal(OrderState.Cancelled, (await db.Orders.SingleAsync()).State);
        }
    }

    [PgFact]
    public async Task Http_order_history_detail_and_cancellation_are_owner_scoped()
    {
        await using var database = await PgDatabase.CreateAsync();
        Guid variantId;
        await using (var db = database.Open()) variantId = (await CatalogTests.PublishedAsync(db)).Variant.Id;
        await using var factory = new CommerceApiFactory(database.ConnectionString);
        using var alice = factory.ShopperClient("alice"); using var bob = factory.ShopperClient("bob");
        await CartTests.SignInAsync(alice); await CartTests.SignInAsync(bob);
        (await alice.PutAsJsonAsync($"/ttb-commerce/v1/cart/lines/{variantId}", new { quantity = 1, expectedVersion = 0 })).EnsureSuccessStatusCode();
        var quoteResponse = await alice.PostAsJsonAsync("/ttb-commerce/v1/checkout/quote", new { cartVersion = 1, buyer = new { name = "Alice", phone = "0812345678" }, delivery = new { method = "pickup" } });
        quoteResponse.EnsureSuccessStatusCode();
        var quoteId = (await quoteResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        alice.DefaultRequestHeaders.Add("Idempotency-Key", "alice-order");
        var orderResponse = await alice.PostAsJsonAsync("/ttb-commerce/v1/orders", new { quoteId, paymentState = "Paid", totalMinor = 1 });
        orderResponse.EnsureSuccessStatusCode();
        var json = await orderResponse.Content.ReadFromJsonAsync<JsonElement>(); var id = json.GetProperty("id").GetGuid();
        Assert.Equal("Unpaid", json.GetProperty("paymentState").GetString()); Assert.Equal(19978, json.GetProperty("totalMinor").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/ttb-commerce/v1/orders/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/ttb-commerce/v1/orders/{id}/cancel", null)).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<JsonElement>("/ttb-commerce/v1/orders")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await alice.PostAsync($"/ttb-commerce/v1/orders/{id}/cancel", null)).StatusCode);
    }

    [PgFact]
    public async Task Hosted_worker_processes_a_due_expiry_and_durably_completes_it()
    {
        await using var database = await PgDatabase.CreateAsync();
        Guid id;
        await using (var db = database.Open())
        {
            var actor = await CartTests.ShopperAsync(db, "alice"); var variant = (await CatalogTests.PublishedAsync(db)).Variant;
            var quote = await QuoteAsync(db, actor, (variant.Id, 1));
            id = (await Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "expiry", default)).Id;
            var past = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.Orders.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, past));
            await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(x => x.AvailableAt, past));
        }
        await using var factory = new CommerceApiFactory(database.ConnectionString, enableWorker: true);
        using var client = factory.ShopperClient(); (await client.GetAsync("/health/live")).EnsureSuccessStatusCode();
        var completed = false;
        for (var i = 0; i < 30; i++)
        {
            await using var db = database.Open();
            completed = await db.Jobs.AnyAsync(x => x.CompletedAt != null);
            if (completed) break;
            await Task.Delay(100);
        }
        Assert.True(completed);
        await using (var db = database.Open())
        {
            Assert.Equal(OrderState.Cancelled, (await db.Orders.SingleAsync(x => x.Id == id)).State);
            Assert.Equal(0, (await db.Variants.SingleAsync()).Reserved);
        }
    }
}
