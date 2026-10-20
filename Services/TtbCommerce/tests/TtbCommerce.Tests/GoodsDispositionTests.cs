using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class GoodsDispositionTests
{
    private sealed record Fixture(ShopperActor Buyer, Guid OrderId, Guid DigitalLine, Guid PhysicalLine, Guid PhysicalVariant, Guid BankId, long Amount, string? AssetKey);
    private static ShopperActor Maker => new(Guid.NewGuid(), "local", "finance-maker", "Maker", ["finance"]);
    private static ShopperActor Checker => new(Guid.NewGuid(), "local", "finance-checker", "Checker", ["finance"]);
    private static string Key() => Guid.NewGuid().ToString("D");
    private static readonly CommerceOptions Commerce = new();
    private static readonly TtbOptions Bank = new();
    private static ManualRefundOptions Policy() => new() { ExternalRefundAttestationApproved = true, GoodsDispositionPolicyApproved = true, LocalSimulationEnabled = true };
    private static ManualRefundService Refunds(CommerceDb db, TestPrivateStorage storage) => new(db, Commerce, Bank, Policy(), TimeProvider.System, storage.Files);
    private static GoodsDispositionService Goods(CommerceDb db, ManualRefundOptions? policy = null) => new(db, Commerce, Bank, policy ?? Policy(), TimeProvider.System);
    private static FulfillmentService Delivery(CommerceDb db, TestPrivateStorage storage) => new(db, [new PhysicalLineDelivery(TimeProvider.System), new DigitalLineDelivery(db, storage.Files, TimeProvider.System)], TimeProvider.System);
    private static async Task<Fixture> MixedAsync(PgDatabase database, TestPrivateStorage storage, bool download = false)
    {
        await using var db = database.Open(); var suffix = Guid.NewGuid().ToString("N")[..10]; var buyer = await CartTests.ShopperAsync(db, "goods-" + suffix); var catalog = new CatalogService(db);
        var physical = (await CatalogTests.PublishedAsync(db, "goods-physical-" + suffix, stock: 5)).Variant;
        DigitalAsset? asset = null;
        if (download) asset = await new DigitalAssetService(db, storage.Files, storage.Options).UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes("Synthetic paid download")), "paid-guide.txt", "test", default);
        var product = await catalog.CreateAsync(CatalogTests.Draft("goods-digital-" + suffix, ProductKind.Digital), "test", default);
        var variant = await catalog.WriteVariantAsync(product.Id, null, new(product.Version, "GOODS-" + suffix.ToUpperInvariant(), "Digital", 1000, true, false, [], download ? DigitalDeliveryKind.Download : DigitalDeliveryKind.InventoryItem, asset?.Key), "test", default);
        await catalog.PublishAsync(product.Id, new(product.Version, true), "test", default);
        var quote = await OrderTests.QuoteAsync(db, buyer, (physical.Id, 2), (variant.Id, 3)); var order = await OrderTests.Service(db).CreateAsync(buyer.ShopperId, new(quote.Id), "goods", default);
        await new QrService(db, new(db, TimeProvider.System), Commerce, Bank, new(Commerce, Bank), TimeProvider.System).GetOrCreateAsync(buyer.ShopperId, order.Id, default);
        await new MockBankService(db, Commerce, new(db, Commerce, Bank, TimeProvider.System), TimeProvider.System).PayAsync(buyer.ShopperId, order.Id, new("goods-pay"), default);
        var lines = await db.OrderLines.AsNoTracking().Where(x => x.OrderId == order.Id).ToArrayAsync(); var credit = await db.BankTransactions.AsNoTracking().SingleAsync();
        return new(buyer, order.Id, lines.Single(x => x.Kind == ProductKind.Digital).Id, lines.Single(x => x.Kind == ProductKind.Physical).Id, physical.Id, credit.Id, credit.AmountMinor, asset?.Key);
    }
    private static async Task<Guid> ApprovedAsync(CommerceDb db, TestPrivateStorage storage, Fixture fixture, long amount, ShopperActor maker)
    {
        var service = Refunds(db, storage); var row = await service.CreateAsync(fixture.BankId, new(Key(), amount.ToString(CultureInfo.InvariantCulture), "Synthetic external refund"), maker, default);
        var evidence = new FinanceEvidenceService(db, Commerce, Bank, Policy(), storage.Files, TimeProvider.System);
        var file = await evidence.UploadAsync(row.CaseId, new MemoryStream(Encoding.UTF8.GetBytes("Synthetic goods refund proof " + row.Id)), "proof.txt", maker, default);
        row = await service.GetAsync(row.Id, maker, default);
        row = await service.ActAsync(row.Id, new(Key(), row.Version, "submit-evidence", "Recorded external transfer", "SYNTHETIC-" + row.Id, DateTimeOffset.UtcNow, [file.Id]), maker, default);
        await service.ActAsync(row.Id, new(Key(), row.Version, "approve-attestation", "Independent check"), Checker, default);
        return await db.Cases.AsNoTracking().Where(x => x.EffectKey == "manual-refund-goods:" + row.Id).Select(x => x.Id).SingleAsync();
    }

    [PgFact]
    public async Task Partial_return_before_workers_reduces_only_selected_units_and_later_workers_cannot_reactivate_them()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await MixedAsync(database, storage); var maker = Maker; Guid caseId;
        await using (var db = database.Open()) caseId = await ApprovedAsync(db, storage, fixture, 2000, maker);
        var input = new GoodsDispositionInput(Key(), 1, "One physical and one digital unit returned; keep remaining goods", false, [new(fixture.DigitalLine, 1)], [new(fixture.PhysicalLine, 1)]);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); return await Goods(db).ActAsync(caseId, input, maker, default); }));
        Assert.Single(results.Select(x => x.OperationId).Distinct()); await using var check = database.Open();
        Assert.Equal(4, (await check.Variants.SingleAsync(x => x.Id == fixture.PhysicalVariant)).OnHand); Assert.Equal(1, await check.GoodsReturns.CountAsync()); Assert.Equal(1, await check.GoodsUnits.CountAsync());
        var digital = await Delivery(check, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default); var physical = await Delivery(check, storage).DispatchAsync(fixture.OrderId, fixture.PhysicalLine, default);
        Assert.Equal(2, digital.Quantity); Assert.Equal(1, physical.Quantity); Assert.Equal(2, await check.Entitlements.CountAsync(x => x.State == EntitlementState.Active));
        Assert.Equal(EntitlementState.Revoked, (await check.Entitlements.SingleAsync(x => x.UnitIndex == 1)).State);
        await Delivery(check, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default); Assert.Equal(3, await check.Entitlements.CountAsync()); Assert.Equal(2, await check.EntitlementOperations.CountAsync(x => x.Action == "grant"));
        var nextCase = await ApprovedAsync(check, storage, fixture, 1000, maker);
        Assert.Equal("goods_return_quantity_invalid", (await Assert.ThrowsAsync<CommerceException>(() => Goods(check).ActAsync(nextCase, new(Key(), 1, "Too many returned units", false, [], [new(fixture.PhysicalLine, 2)]), maker, default))).Code);
        Assert.Equal("goods_unit_invalid", (await Assert.ThrowsAsync<CommerceException>(() => Goods(check).ActAsync(nextCase, new(Key(), 1, "Cannot cancel the same digital unit twice", false, [new(fixture.DigitalLine, 1)], []), maker, default))).Code);
        await Goods(check).ActAsync(nextCase, new(Key(), 1, "Return the final physical unit", false, [], [new(fixture.PhysicalLine, 1)]), maker, default);
        Assert.Equal(5, (await check.Variants.SingleAsync(x => x.Id == fixture.PhysicalVariant)).OnHand); Assert.Equal(FulfillmentState.Cancelled, (await check.Fulfillments.SingleAsync(x => x.OrderLineId == fixture.PhysicalLine)).State);
        Assert.Equal(OrderState.Completed, (await check.Orders.SingleAsync()).State); Assert.Equal(PaymentState.Paid, (await check.Orders.SingleAsync()).PaymentState);
    }

    [PgFact]
    public async Task Full_external_refund_cancels_pending_jobs_without_grants_and_restocks_only_explicit_units_even_when_paid_file_is_missing()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await MixedAsync(database, storage, download: true); var maker = Maker;
        File.Delete(storage.Files.PathFor("assets", fixture.AssetKey!, ".asset")); await using var db = database.Open(); var caseId = await ApprovedAsync(db, storage, fixture, fixture.Amount, maker);
        Assert.Equal("goods_cancellation_scope_invalid", (await Assert.ThrowsAsync<CommerceException>(() => Goods(db).ActAsync(caseId, new(Key(), 1, "Must cancel undelivered goods", false, [], []), maker, default))).Code);
        await Goods(db).ActAsync(caseId, new(Key(), 1, "Cancel all undelivered goods; only one physical unit is resalable", true, [], [new(fixture.PhysicalLine, 1)]), maker, default);
        Assert.Equal(PaymentState.Refunded, (await db.Orders.SingleAsync()).PaymentState); Assert.Equal(OrderState.Cancelled, (await db.Orders.SingleAsync()).State);
        Assert.Equal(4, (await db.Variants.SingleAsync(x => x.Id == fixture.PhysicalVariant)).OnHand); Assert.Equal(3, await db.Entitlements.CountAsync(x => x.State == EntitlementState.Revoked));
        Assert.Equal("Cancelled", (await Delivery(db, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default)).State);
        Assert.Equal("Cancelled", (await Delivery(db, storage).DispatchAsync(fixture.OrderId, fixture.PhysicalLine, default)).State);
        Assert.Equal(0, await db.EntitlementOperations.CountAsync(x => x.Action == "grant")); Assert.Equal(1, await db.GoodsReturns.CountAsync()); Assert.False((await db.BankTransactions.SingleAsync()).Voided);
    }

    [PgFact]
    public async Task Downloaded_units_require_explicit_review_and_return_policy_and_revocation_keeps_other_units_active()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await MixedAsync(database, storage, download: true); var maker = Maker;
        await using var db = database.Open(); await Delivery(db, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default); var grant = await db.Entitlements.SingleAsync(x => x.UnitIndex == 0);
        var download = await new EntitlementService(db, storage.Files, TimeProvider.System).DownloadAsync(fixture.Buyer.ShopperId, grant.Id, "test", default); await download.Stream.DisposeAsync();
        var caseId = await ApprovedAsync(db, storage, fixture, 1000, maker); var input = new GoodsDispositionInput(Key(), 1, "Revoke only the returned downloaded unit", false, [new(fixture.DigitalLine, 0)], []);
        Assert.Equal("goods_policy_unapproved", (await Assert.ThrowsAsync<CommerceException>(() => Goods(db, new()).ActAsync(caseId, input, maker, default))).Code);
        Assert.Equal("goods_consumed_review_required", (await Assert.ThrowsAsync<CommerceException>(() => Goods(db).ActAsync(caseId, input, maker, default))).Code);
        await Goods(db).ActAsync(caseId, input with { ConsumedGoodsReviewed = true }, maker, default);
        Assert.Equal(1, await db.Entitlements.CountAsync(x => x.State == EntitlementState.Revoked)); Assert.Equal(2, await db.Entitlements.CountAsync(x => x.State == EntitlementState.Active));
        Assert.Equal(FulfillmentState.Delivered, (await db.Fulfillments.SingleAsync(x => x.OrderLineId == fixture.DigitalLine)).State);
        Assert.Equal(2, (await Goods(db).GetAsync(caseId, maker, default)).CaseVersion);
    }

    [PgFact]
    public async Task Case_version_foreign_units_and_roles_cannot_override_a_goods_review()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await MixedAsync(database, storage); var maker = Maker;
        await using var db = database.Open(); var caseId = await ApprovedAsync(db, storage, fixture, 1000, maker); var input = new GoodsDispositionInput(Key(), 1, "Goods review", false, [new(Guid.NewGuid(), 0)], []);
        Assert.Equal("role_required", (await Assert.ThrowsAsync<CommerceException>(() => Goods(db).GetAsync(caseId, fixture.Buyer, default))).Code);
        Assert.Equal("goods_unit_invalid", (await Assert.ThrowsAsync<CommerceException>(() => Goods(db).ActAsync(caseId, input, maker, default))).Code);
        await new FinanceService(db, Commerce, Bank, TimeProvider.System).CaseActionAsync(caseId, new(Key(), 1, "note", "Another officer added evidence"), maker, default);
        Assert.Equal("case_changed", (await Assert.ThrowsAsync<CommerceException>(() => Goods(db).ActAsync(caseId, input with { RevokeDigitalUnits = [] }, maker, default))).Code);
        Assert.Equal(0, await db.GoodsReturns.CountAsync()); Assert.Equal(0, await db.GoodsUnits.CountAsync()); Assert.Equal(0, await db.Entitlements.CountAsync());
    }
}
