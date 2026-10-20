using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class MockBankTests
{
    private sealed record LocalFixture(ShopperActor Actor, Guid OrderId, Guid VariantId);
    private static async Task<LocalFixture> CreateAsync(PgDatabase database)
    {
        await using var db = database.Open(); var suffix = Guid.NewGuid().ToString("N")[..10];
        var actor = await CartTests.ShopperAsync(db, "local-" + suffix); var variant = (await CatalogTests.PublishedAsync(db, "local-" + suffix)).Variant;
        var quote = await OrderTests.QuoteAsync(db, actor, (variant.Id, 1)); var order = await OrderTests.Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "local-checkout", default);
        var options = new CommerceOptions(); var ttb = new TtbOptions();
        await new QrService(db, new(db, TimeProvider.System), options, ttb, new(options, ttb), TimeProvider.System).GetOrCreateAsync(actor.ShopperId, order.Id, default);
        return new(actor, order.Id, variant.Id);
    }
    private static MockBankService Simulator(CommerceDb db, CommerceOptions? options = null) => new(db, options ?? new(), new(db, options ?? new(), new(), TimeProvider.System), TimeProvider.System);

    [PgFact]
    public async Task Concurrent_mock_retries_persist_one_credit_and_allocate_once()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await CreateAsync(database);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = database.Open(); return await Simulator(db).PayAsync(fixture.Actor.ShopperId, fixture.OrderId, new("one-click"), default);
        }));
        Assert.Single(results.Select(x => x.InstructionId).Distinct()); Assert.All(results, x => Assert.Equal("Mock", x.Mode));
        await using var check = database.Open(); Assert.Equal(1, await check.MockCredits.CountAsync()); Assert.Equal(1, await check.BankTransactions.CountAsync()); Assert.Equal(1, await check.Allocations.CountAsync());
        Assert.Equal(4, (await check.Variants.SingleAsync()).OnHand);
    }

    [PgFact]
    public async Task Lost_mock_callback_can_be_recovered_by_bounded_Verify_without_faking_a_notification_ACK()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await CreateAsync(database);
        await using (var db = database.Open()) Assert.Equal("CreditAtSimulatorOnly", (await Simulator(db).PayAsync(fixture.Actor.ShopperId, fixture.OrderId, new("lost", "lost-callback"), default)).Outcome);
        await using (var db = database.Open()) Assert.Equal(0, await db.Allocations.CountAsync());
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(61)); var options = new CommerceOptions();
        await using (var db = database.Open())
        {
            var payments = new PaymentProcessor(db, options, new(), clock);
            var service = new VerificationService(db, options, new(), new MockBankGateway(db, options, clock), payments, clock);
            Assert.Equal("Paid", (await service.CheckAsync(fixture.Actor.ShopperId, fixture.OrderId, new(), default)).Outcome);
        }
        await using var check = database.Open(); Assert.Equal(1, await check.Allocations.CountAsync()); Assert.Equal(0, await check.CallbackReceipts.CountAsync());
        Assert.Equal("MockVerify", (await check.Observations.SingleAsync()).Source);
    }

    [PgFact]
    public async Task Duplicate_and_amount_mismatch_scenarios_use_the_same_financial_invariants()
    {
        await using var database = await PgDatabase.CreateAsync(); var first = await CreateAsync(database); var second = await CreateAsync(database);
        await using (var db = database.Open())
        {
            var result = await Simulator(db).PayAsync(first.Actor.ShopperId, first.OrderId, new("duplicate", "duplicate"), default);
            Assert.Equal("0000", result.AckCode); Assert.Equal("0004", result.DuplicateAckCode);
        }
        await using (var db = database.Open()) Assert.Equal("0005", (await Simulator(db).PayAsync(second.Actor.ShopperId, second.OrderId, new("wrong", "amount-mismatch"), default)).AckCode);
        await using var check = database.Open(); Assert.Equal(1, await check.Allocations.CountAsync()); Assert.Equal(1, await check.Cases.CountAsync());
    }

    [PgFact]
    public async Task Mock_cannot_run_in_bank_UAT_or_access_another_shoppers_order()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await CreateAsync(database);
        await using (var db = database.Open()) Assert.Equal(404, (await Assert.ThrowsAsync<CommerceException>(() => Simulator(db, PaymentFixture.Commerce).PayAsync(fixture.Actor.ShopperId, fixture.OrderId, new("bad"), default))).Status);
        await using (var db = database.Open()) Assert.Equal(404, (await Assert.ThrowsAsync<CommerceException>(() => Simulator(db).PayAsync(Guid.NewGuid(), fixture.OrderId, new("other-owner"), default))).Status);
        await using var check = database.Open(); Assert.Equal(0, await check.MockCredits.CountAsync());
    }

    [PgFact]
    public async Task HTTP_vertical_slice_buys_a_mixed_cart_and_history_remains_owner_private()
    {
        await using var database = await PgDatabase.CreateAsync(); Guid physical; Guid digital;
        await using (var db = database.Open())
        {
            physical = (await CatalogTests.PublishedAsync(db, "physical")).Variant.Id;
            digital = (await CatalogTests.PublishedAsync(db, "digital", ProductKind.Digital)).Variant.Id;
        }
        await using var factory = new CommerceApiFactory(database.ConnectionString);
        using var alice = factory.ShopperClient(); using var bob = factory.ShopperClient();
        static async Task LocalSignIn(HttpClient client, string subject)
        {
            var response = await client.PostAsJsonAsync("/ttb-commerce/v1/session/local", new { subject }); response.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Add("X-CSRF-Token", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString());
        }
        await LocalSignIn(alice, "alice"); await LocalSignIn(bob, "bob");
        (await alice.PutAsJsonAsync($"/ttb-commerce/v1/cart/lines/{physical}", new { quantity = 1, expectedVersion = 0 })).EnsureSuccessStatusCode();
        (await alice.PutAsJsonAsync($"/ttb-commerce/v1/cart/lines/{digital}", new { quantity = 2, expectedVersion = 1 })).EnsureSuccessStatusCode();
        var quoteResponse = await alice.PostAsJsonAsync("/ttb-commerce/v1/checkout/quote", new { cartVersion = 2, buyer = new { name = "ผู้ซื้อทดสอบ", phone = "0812345678" }, delivery = new { method = "pickup" } }); quoteResponse.EnsureSuccessStatusCode();
        var quote = await quoteResponse.Content.ReadFromJsonAsync<JsonElement>(); alice.DefaultRequestHeaders.Add("Idempotency-Key", "mixed-http");
        var orderResponse = await alice.PostAsJsonAsync("/ttb-commerce/v1/orders", new { quoteId = quote.GetProperty("id").GetGuid() }); orderResponse.EnsureSuccessStatusCode();
        var orderId = (await orderResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var qrResponse = await alice.PostAsync($"/ttb-commerce/v1/orders/{orderId}/qr", null); qrResponse.EnsureSuccessStatusCode();
        var qr = await qrResponse.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Mock", qr.GetProperty("mode").GetString()); Assert.StartsWith("MANGO-MOCK:", qr.GetProperty("payload").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/ttb-commerce/v1/orders/{orderId}/mock-pay", new { requestKey = "steal" })).StatusCode);
        var paid = await alice.PostAsJsonAsync($"/ttb-commerce/v1/orders/{orderId}/mock-pay", new { requestKey = "pay-once", amountMinor = 1, paymentState = "Paid" }); paid.EnsureSuccessStatusCode();
        var detail = await alice.GetFromJsonAsync<JsonElement>($"/ttb-commerce/v1/orders/{orderId}"); Assert.Equal("Paid", detail.GetProperty("paymentState").GetString()); Assert.Equal(59934, detail.GetProperty("totalMinor").GetInt64());
        Assert.Single((await alice.GetFromJsonAsync<JsonElement>("/ttb-commerce/v1/orders")).GetProperty("items").EnumerateArray());
        Assert.Empty((await bob.GetFromJsonAsync<JsonElement>("/ttb-commerce/v1/orders")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsync($"/ttb-commerce/v1/orders/{orderId}/cancel", null)).StatusCode);
        await using var check = database.Open(); Assert.Equal(4, await check.Jobs.CountAsync()); // expiry, receipt, two fulfillment lines
    }
}
