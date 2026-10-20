using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class FulfillmentTests
{
    internal static FulfillmentService Service(CommerceDb db) => new(db, [new PhysicalLineDelivery(TimeProvider.System)], TimeProvider.System);
    private static async Task<Guid> LineAsync(CommerceDb db, Guid orderId) => await db.OrderLines.Where(x => x.OrderId == orderId).Select(x => x.Id).SingleAsync();
    private static async Task PayAsync(PgDatabase database, PaymentFixture fixture)
    {
        await using var db = database.Open(); await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default);
    }

    [PgFact]
    public async Task Unpaid_line_cannot_dispatch_and_concurrent_paid_retries_create_one_delivery()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database); Guid line;
        await using (var db = database.Open())
        {
            line = await LineAsync(db, fixture.OrderId);
            Assert.Equal("fulfillment_not_ready", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).DispatchAsync(fixture.OrderId, line, default))).Code);
        }
        await PayAsync(database, fixture);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); return await Service(db).DispatchAsync(fixture.OrderId, line, default); }));
        Assert.Single(results.Select(x => x.Id).Distinct()); Assert.All(results, x => Assert.Equal("Pending", x.State));
        await using var check = database.Open(); Assert.Equal(1, await check.Fulfillments.CountAsync()); Assert.Equal(4, (await check.Variants.SingleAsync()).OnHand);
    }

    [PgFact]
    public async Task Pickup_transitions_are_audited_and_completion_is_separate_from_payment()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database); await PayAsync(database, fixture);
        await using var db = database.Open(); var service = Service(db); var item = await service.DispatchAsync(fixture.OrderId, await LineAsync(db, fixture.OrderId), default);
        Assert.Equal(PaymentState.Paid, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState); Assert.Equal(OrderState.Open, (await db.Orders.AsNoTracking().SingleAsync()).State);
        item = await service.ActAsync(item.Id, new(item.Version, "prepare"), "mangosgo:warehouse", default);
        await Assert.ThrowsAsync<CommerceException>(() => service.ActAsync(item.Id, new(1, "ready-pickup"), "mangosgo:warehouse", default));
        item = await service.ActAsync(item.Id, new(item.Version, "ready-pickup"), "mangosgo:warehouse", default);
        item = await service.ActAsync(item.Id, new(item.Version, "deliver", Note: "รับสินค้าครบแล้ว"), "mangosgo:warehouse", default);
        Assert.Equal("Delivered", item.State); Assert.NotNull(item.DeliveredAt);
        Assert.Equal(OrderState.Completed, (await db.Orders.AsNoTracking().SingleAsync()).State); Assert.Equal(PaymentState.Paid, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState);
        Assert.Equal(3, await db.AuditEvents.CountAsync(x => x.Actor == "mangosgo:warehouse"));
    }

    [PgFact]
    public async Task Shipping_requires_tracking_and_cannot_use_pickup_transitions()
    {
        await using var database = await PgDatabase.CreateAsync(); Guid orderId; Guid owner;
        await using (var db = database.Open())
        {
            var actor = await CartTests.ShopperAsync(db, "shipper"); owner = actor.ShopperId; var variant = (await CatalogTests.PublishedAsync(db)).Variant;
            var options = new CommerceOptions { ShippingEnabled = true, ShippingFeeMinor = 5000 }; var cart = new CartService(db, options, TimeProvider.System);
            await cart.ChangeAsync(owner, variant.Id, new(1, 0), default);
            var quote = await cart.QuoteAsync(actor, new(1, new("ผู้รับ", Phone: "0812345678"), new("shipping", new("99 อาคารA", "ปทุมวัน", "ปทุมวัน", "กรุงเทพมหานคร", "10330"))), default);
            orderId = (await OrderTests.Service(db, options).CreateAsync(owner, new(quote.Id), "ship", default)).Id;
            var ttb = new TtbOptions(); await new QrService(db, new(db, TimeProvider.System), options, ttb, new(options, ttb), TimeProvider.System).GetOrCreateAsync(owner, orderId, default);
            await new MockBankService(db, options, new(db, options, ttb, TimeProvider.System), TimeProvider.System).PayAsync(owner, orderId, new("ship-pay"), default);
        }
        await using var check = database.Open(); var service = Service(check); var item = await service.DispatchAsync(orderId, await LineAsync(check, orderId), default);
        item = await service.ActAsync(item.Id, new(item.Version, "prepare"), "operator", default);
        await Assert.ThrowsAsync<CommerceException>(() => service.ActAsync(item.Id, new(item.Version, "ready-pickup"), "operator", default));
        Assert.Equal("tracking_required", (await Assert.ThrowsAsync<CommerceException>(() => service.ActAsync(item.Id, new(item.Version, "ship"), "operator", default))).Code);
        item = await service.ActAsync(item.Id, new(item.Version, "ship", "ขนส่งทดสอบ", "TRACK123"), "operator", default);
        Assert.Equal("Shipped", item.State); Assert.NotNull(item.ShippedAt); Assert.Equal("TRACK123", item.TrackingNumber);
        item = await service.ActAsync(item.Id, new(item.Version, "deliver"), "operator", default); Assert.Equal("Delivered", item.State);
    }

    [PgFact]
    public async Task A_new_finance_hold_blocks_an_operator_with_an_older_tracked_order()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database); await PayAsync(database, fixture);
        await using var db = database.Open(); var service = Service(db); var item = await service.DispatchAsync(fixture.OrderId, await LineAsync(db, fixture.OrderId), default);
        await using (var finance = database.Open()) await finance.Orders.Where(x => x.Id == fixture.OrderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, OrderState.Review));
        Assert.Equal("fulfillment_not_ready", (await Assert.ThrowsAsync<CommerceException>(() => service.ActAsync(item.Id, new(item.Version, "prepare"), "operator", default))).Code);
        Assert.Equal("Pending", (await service.OwnAsync(fixture.ShopperId, fixture.OrderId, default)).Single().State);
    }

    [PgFact]
    public async Task HTTP_queue_and_operator_actions_require_roles_while_shopper_status_requires_ownership()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database); await PayAsync(database, fixture); FulfillmentView item;
        await using (var db = database.Open())
        {
            await db.Shoppers.Where(x => x.Id == fixture.ShopperId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Issuer, "mangosgo").SetProperty(x => x.Subject, "alice"));
            item = await Service(db).DispatchAsync(fixture.OrderId, await LineAsync(db, fixture.OrderId), default);
        }
        await using var factory = new CommerceApiFactory(database.ConnectionString, environment: CommerceEnvironment.Uat);
        using var alice = factory.ShopperClient("alice"); using var bob = factory.ShopperClient("bob"); using var admin = factory.ShopperClient("admin");
        await CartTests.SignInAsync(alice); await CartTests.SignInAsync(bob); await CartTests.SignInAsync(admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/ttb-commerce/v1/admin/fulfillment")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/ttb-commerce/v1/orders/{fixture.OrderId}/fulfillment")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/ttb-commerce/v1/orders/{fixture.OrderId}/fulfillment")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsJsonAsync($"/ttb-commerce/v1/admin/fulfillment/{item.Id}/actions", new { expectedVersion = item.Version, action = "prepare" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/ttb-commerce/v1/admin/fulfillment/{item.Id}/actions", new { expectedVersion = item.Version, action = "prepare" })).StatusCode);
    }
}
