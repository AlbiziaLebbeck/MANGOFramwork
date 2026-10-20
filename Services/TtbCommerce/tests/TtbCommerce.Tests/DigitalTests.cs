using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class TestPrivateStorage : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ttb-unit-storage-" + Guid.NewGuid().ToString("N"));
    public PrivateStorageOptions Options => new() { RootPath = Root };
    public PrivateFiles Files => new(Options);
    public void Dispose()
    {
        var full = Path.GetFullPath(Root); var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(parent, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !Path.GetFileName(full).StartsWith("ttb-unit-storage-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing unexpected test storage cleanup.");
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }
}

public sealed class DigitalTests
{
    private sealed record DigitalFixture(ShopperActor Actor, Guid OrderId, Guid DigitalLine, Guid? PhysicalLine, DigitalAsset? Asset, byte[] Bytes);
    private static FulfillmentService Delivery(CommerceDb db, TestPrivateStorage storage) => new(db,
        [new PhysicalLineDelivery(TimeProvider.System), new DigitalLineDelivery(db, storage.Files, TimeProvider.System)], TimeProvider.System);

    private static async Task<DigitalFixture> CreateAsync(PgDatabase database, TestPrivateStorage storage, bool download = true, bool mixed = false, int quantity = 2)
    {
        await using var db = database.Open(); var suffix = Guid.NewGuid().ToString("N")[..10]; var actor = await CartTests.ShopperAsync(db, "digital-" + suffix);
        var bytes = Encoding.UTF8.GetBytes("ไฟล์ดิจิทัลทดสอบที่ต้องซื้อก่อนดาวน์โหลด"); DigitalAsset? asset = null;
        if (download) asset = await new DigitalAssetService(db, storage.Files, storage.Options).UploadAsync(new MemoryStream(bytes), "ไฟล์ส่วนตัว.txt", "test:admin", default);
        var catalog = new CatalogService(db); var product = await catalog.CreateAsync(CatalogTests.Draft("digital-" + suffix, ProductKind.Digital), "test:admin", default);
        var variant = await catalog.WriteVariantAsync(product.Id, null, new(product.Version, "DIGITAL-" + suffix.ToUpperInvariant(), "Digital", 1000, true, false, [],
            download ? DigitalDeliveryKind.Download : DigitalDeliveryKind.InventoryItem, asset?.Key), "test:admin", default);
        await catalog.PublishAsync(product.Id, new(product.Version, true), "test:admin", default);
        var cartLines = new List<(Guid, int)> { (variant.Id, quantity) };
        if (mixed) cartLines.Add(((await CatalogTests.PublishedAsync(db, "physical-" + suffix)).Variant.Id, 1));
        var quote = await OrderTests.QuoteAsync(db, actor, cartLines.ToArray()); var order = await OrderTests.Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "digital-buy", default);
        var options = new CommerceOptions(); var ttb = new TtbOptions();
        await new QrService(db, new(db, TimeProvider.System), options, ttb, new(options, ttb), TimeProvider.System).GetOrCreateAsync(actor.ShopperId, order.Id, default);
        await new MockBankService(db, options, new(db, options, ttb, TimeProvider.System), TimeProvider.System).PayAsync(actor.ShopperId, order.Id, new("digital-pay"), default);
        var lines = await db.OrderLines.Where(x => x.OrderId == order.Id).ToArrayAsync();
        return new(actor, order.Id, lines.Single(x => x.Kind == ProductKind.Digital).Id, lines.SingleOrDefault(x => x.Kind == ProductKind.Physical)?.Id, asset, bytes);
    }

    [PgFact]
    public async Task Digital_quantity_is_granted_per_line_unit_once_and_does_not_complete_physical_lines()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage, mixed: true, quantity: 3);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); await Delivery(db, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default); }));
        await using var check = database.Open(); Assert.Equal(3, await check.Entitlements.CountAsync()); Assert.Equal(3, (await check.Entitlements.Select(x => x.UnitIndex).ToArrayAsync()).Distinct().Count());
        Assert.Equal(OrderState.Open, (await check.Orders.SingleAsync()).State); Assert.Equal(PaymentState.Paid, (await check.Orders.SingleAsync()).PaymentState);
        var physical = await Delivery(check, storage).DispatchAsync(fixture.OrderId, fixture.PhysicalLine!.Value, default); Assert.Equal("Pending", physical.State);
        Assert.Equal(2, await check.Fulfillments.CountAsync());
    }

    [PgFact]
    public async Task Downloads_require_active_owned_entitlements_and_are_audited_as_requests()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage);
        await using (var db = database.Open()) await Delivery(db, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default);
        Guid id;
        await using (var db = database.Open())
        {
            id = await db.Entitlements.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(); var service = new EntitlementService(db, storage.Files, TimeProvider.System);
            Assert.Equal(404, (await Assert.ThrowsAsync<CommerceException>(() => service.DownloadAsync(Guid.NewGuid(), id, "wrong-owner", default))).Status);
            var opened = await service.DownloadAsync(fixture.Actor.ShopperId, id, "owned-request", default);
            await using (opened.Stream) { using var output = new MemoryStream(); await opened.Stream.CopyToAsync(output); Assert.Equal(fixture.Bytes, output.ToArray()); }
        }
        await using (var db = database.Open())
        {
            var grant = await db.Entitlements.SingleAsync(x => x.Id == id); Assert.Equal(1, grant.DownloadRequests); Assert.NotNull(grant.DownloadRequestedAt);
            Assert.Equal(1, await db.DownloadEvents.CountAsync()); grant.State = EntitlementState.Revoked; await db.SaveChangesAsync();
        }
        await using (var db = database.Open()) Assert.Equal("download_not_available", (await Assert.ThrowsAsync<CommerceException>(() => new EntitlementService(db, storage.Files, TimeProvider.System).DownloadAsync(fixture.Actor.ShopperId, id, "revoked", default))).Code);
    }

    [PgFact]
    public async Task Missing_or_corrupt_paid_asset_retries_without_granting_or_charging_again()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage);
        var path = storage.Files.PathFor("assets", fixture.Asset!.Key, ".asset"); await File.WriteAllBytesAsync(path, new byte[fixture.Bytes.Length]);
        await using (var db = database.Open()) Assert.Equal("digital_asset_unavailable", (await Assert.ThrowsAsync<CommerceException>(() => Delivery(db, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default))).Code);
        await using (var db = database.Open()) { Assert.Equal(0, await db.Entitlements.CountAsync()); Assert.Equal(PaymentState.Paid, (await db.Orders.SingleAsync()).PaymentState); }
        await File.WriteAllBytesAsync(path, fixture.Bytes);
        await using (var db = database.Open()) await Delivery(db, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default);
        await using (var db = database.Open()) { Assert.Equal(2, await db.Entitlements.CountAsync()); Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Equal(OrderState.Completed, (await db.Orders.SingleAsync()).State); }
    }

    [PgFact]
    public async Task Asset_upload_limits_and_private_identifiers_cannot_escape_storage()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); await using var db = database.Open();
        var service = new DigitalAssetService(db, storage.Files, new() { RootPath = storage.Root, MaxAssetBytes = 4 });
        Assert.Equal(413, (await Assert.ThrowsAsync<CommerceException>(() => service.UploadAsync(new MemoryStream(new byte[5]), "file.zip", "admin", default))).Status);
        await Assert.ThrowsAsync<CommerceException>(() => service.UploadAsync(new MemoryStream([1]), "script.html", "admin", default));
        Assert.Throws<ArgumentException>(() => storage.Files.PathFor("assets", "../secret", ".asset"));
        Assert.Equal(0, await db.DigitalAssets.CountAsync()); Assert.Empty(Directory.GetFiles(Path.Combine(storage.Root, "assets")));
    }

    [PgFact]
    public async Task Revocation_operation_is_replayable_and_blocks_download_and_game_use()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage);
        await using (var db = database.Open()) await Delivery(db, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default);
        await using var check = database.Open(); var id = await check.Entitlements.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var service = new EntitlementService(check, storage.Files, TimeProvider.System);
        await service.RevokeAsync(id, new("one-revocation", "การทดสอบเพิกถอน"), "finance", default);
        await service.RevokeAsync(id, new("one-revocation", "การทดสอบเพิกถอน"), "finance", default);
        Assert.Equal(1, await check.EntitlementOperations.CountAsync(x => x.Action == "revoke"));
        Assert.Equal(2, await check.EntitlementOperations.CountAsync(x => x.Action == "grant"));
        Assert.Equal("idempotency_conflict", (await Assert.ThrowsAsync<CommerceException>(() => service.RevokeAsync(id, new("one-revocation", "เปลี่ยนข้อมูล"), "finance", default))).Code);
        await Assert.ThrowsAsync<CommerceException>(() => service.DownloadAsync(fixture.Actor.ShopperId, id, "revoked", default));
        var result = JsonSerializer.SerializeToElement(await service.ValidateGameAsync(fixture.Actor.ShopperId, id, default)); Assert.False(result.GetProperty("owned").GetBoolean());
        Assert.Equal(PaymentState.Paid, (await check.Orders.AsNoTracking().SingleAsync()).PaymentState); // revocation itself never invents a refund
    }

    [PgFact]
    public async Task Buying_the_same_digital_product_again_grants_additional_units()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage, download: false, quantity: 1);
        await using var db = database.Open(); await Delivery(db, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default);
        var variant = await db.OrderLines.Where(x => x.Id == fixture.DigitalLine).Select(x => x.VariantId).SingleAsync();
        var quote = await OrderTests.QuoteAsync(db, fixture.Actor, (variant, 2)); var order = await OrderTests.Service(db).CreateAsync(fixture.Actor.ShopperId, new(quote.Id), "buy-again", default);
        var options = new CommerceOptions(); var ttb = new TtbOptions();
        await new QrService(db, new(db, TimeProvider.System), options, ttb, new(options, ttb), TimeProvider.System).GetOrCreateAsync(fixture.Actor.ShopperId, order.Id, default);
        await new MockBankService(db, options, new(db, options, ttb, TimeProvider.System), TimeProvider.System).PayAsync(fixture.Actor.ShopperId, order.Id, new("pay-again"), default);
        var line = await db.OrderLines.Where(x => x.OrderId == order.Id).Select(x => x.Id).SingleAsync(); await Delivery(db, storage).DispatchAsync(order.Id, line, default);
        Assert.Equal(3, await db.Entitlements.CountAsync()); Assert.Equal(3, await db.EntitlementOperations.CountAsync(x => x.Action == "grant"));
    }

    [PgFact]
    public async Task HTTP_private_download_and_game_validation_do_not_trust_a_shopper_claim()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage);
        Guid id;
        await using (var db = database.Open())
        {
            await db.Shoppers.Where(x => x.Id == fixture.Actor.ShopperId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Issuer, "mangosgo").SetProperty(x => x.Subject, "alice"));
            await Delivery(db, storage).DispatchAsync(fixture.OrderId, fixture.DigitalLine, default); id = await db.Entitlements.Select(x => x.Id).FirstAsync();
        }
        const string gameKey = "unit-test-game-key-must-never-be-shipped";
        await using var factory = new CommerceApiFactory(database.ConnectionString) { PrivateRoot = storage.Root, GameKey = gameKey };
        using var alice = factory.ShopperClient("alice"); using var bob = factory.ShopperClient("bob"); await CartTests.SignInAsync(alice); await CartTests.SignInAsync(bob);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/ttb-commerce/v1/inventory/{id}/download")).StatusCode);
        var response = await alice.GetAsync($"/ttb-commerce/v1/inventory/{id}/download"); response.EnsureSuccessStatusCode(); Assert.Equal(fixture.Bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType); Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/assets/{fixture.Asset!.Key}.asset")).StatusCode);
        var body = new { shopperId = fixture.Actor.ShopperId, entitlementId = id, paid = true };
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.PostAsJsonAsync("/ttb-commerce/v1/game/entitlements/validate", body)).StatusCode);
        using var game = factory.ShopperClient(); game.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gameKey);
        var owned = await game.PostAsJsonAsync("/ttb-commerce/v1/game/entitlements/validate", body); owned.EnsureSuccessStatusCode();
        Assert.True((await owned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("owned").GetBoolean());
        var wrong = await game.PostAsJsonAsync("/ttb-commerce/v1/game/entitlements/validate", new { shopperId = Guid.NewGuid(), entitlementId = id });
        Assert.False((await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("owned").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsJsonAsync($"/ttb-commerce/v1/admin/entitlements/{id}/revoke", new { operationId = "alice-denied", reason = "not allowed" })).StatusCode);
        using var admin = factory.ShopperClient("admin"); await CartTests.SignInAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/ttb-commerce/v1/admin/entitlements/{id}/revoke", new { operationId = "admin-revoke", reason = "ทดสอบเพิกถอน" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.GetAsync($"/ttb-commerce/v1/inventory/{id}/download")).StatusCode);
    }
}
