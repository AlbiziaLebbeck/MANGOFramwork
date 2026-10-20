using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class IssuerProfileTests
{
    private static IssuerProfileInput Input(long version = 0, string name = "บริษัททดสอบใหม่") =>
        new(version, name, "ที่อยู่จำลอง\nกรุงเทพมหานคร 10000", "0000000000000", "สำนักงานใหญ่ (ทดสอบ)", "test@example.invalid");
    private static IssuerProfileService Service(CommerceDb db, CommerceEnvironment environment = CommerceEnvironment.Local) => new(db, new() { Environment = environment }, new(), TimeProvider.System);

    [PgFact]
    public async Task Concurrent_first_edit_has_one_winner_and_version_prevents_lost_updates()
    {
        await using var database = await PgDatabase.CreateAsync();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            await using var db = database.Open();
            try { await Service(db).SaveAsync(Input(name: "Test " + i), "admin", default); return "saved"; }
            catch (CommerceException error) { return error.Code; }
        }));
        Assert.Single(outcomes, x => x == "saved"); Assert.Single(outcomes, x => x == "issuer_profile_changed");
        await using var check = database.Open(); var service = Service(check);
        Assert.Equal(1, (await service.GetAsync(default)).Version);
        var next = await service.SaveAsync(Input(1), "admin", default); Assert.Equal(2, next.Version); Assert.False(next.Approved);
        Assert.Equal("issuer_profile_changed", (await Assert.ThrowsAsync<CommerceException>(() => service.SaveAsync(Input(1), "admin", default))).Code);
        Assert.Equal(1, await check.TestIssuerProfiles.CountAsync()); Assert.Equal(2, await check.AuditEvents.CountAsync(x => x.Action == "test-issuer.update"));
    }

    [PgFact]
    public async Task Issued_snapshot_stays_immutable_after_profile_edit_and_contains_branch_and_contact()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage();
        var payment = await PaymentFixture.CreateAsync(database); await using var db = database.Open();
        await PaymentFixture.Processor(db).NoticeAsync(payment.Raw, PaymentFixture.Metadata(), default);
        await Service(db).SaveAsync(Input(), "admin", default);
        var receipts = new ReceiptService(db, new(db, TimeProvider.System), new(), new(), TimeProvider.System, storage.Files);
        await receipts.EnsureAsync(payment.OrderId, default);
        var original = await db.Documents.AsNoTracking().SingleAsync(); var snapshot = ReceiptTemplate.Read(original);
        Assert.Equal(Input().Name, snapshot.Issuer.Name); Assert.False(snapshot.Issuer.Approved);
        Assert.Contains(Input().Branch, snapshot.Issuer.Address); Assert.Contains(Input().Contact, snapshot.Issuer.Address);
        Assert.Contains("ข้อมูลผู้ออกเอกสารยังไม่ผ่านการรับรอง", ReceiptTemplate.Html(snapshot));
        await Service(db).SaveAsync(Input(1, "เปลี่ยนชื่อบริษัทแล้ว"), "admin", default);
        Assert.Equal("เปลี่ยนชื่อบริษัทแล้ว", (await Service(db).SnapshotAsync(default)).Name);
        await receipts.EnsureAsync(payment.OrderId, default);
        var unchanged = await db.Documents.AsNoTracking().SingleAsync(); Assert.Equal(original.SnapshotHash, unchanged.SnapshotHash); Assert.Equal(original.SnapshotJson, unchanged.SnapshotJson);
        Assert.Equal(snapshot.Issuer, ReceiptTemplate.Read(unchanged).Issuer);
    }

    [PgFact]
    public async Task Profile_HTTP_requires_admin_CSRF_and_returns_persisted_unapproved_test_fields()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var factory = new CommerceApiFactory(database.ConnectionString);
        using var anonymous = factory.ShopperClient(); using var buyer = factory.ShopperClient("buyer"); using var admin = factory.ShopperClient("admin");
        const string path = "/ttb-commerce/v1/admin/issuer-profile";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        await CartTests.SignInAsync(buyer); await CartTests.SignInAsync(admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PutAsJsonAsync(path, Input())).StatusCode);
        var before = (await admin.GetFromJsonAsync<IssuerProfileView>(path))!; Assert.True(before.Editable); Assert.False(before.Approved); Assert.Equal(0, before.Version);
        var csrf = admin.DefaultRequestHeaders.GetValues("X-CSRF-Token").Single(); admin.DefaultRequestHeaders.Remove("X-CSRF-Token");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync(path, Input())).StatusCode);
        admin.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
        var saved = await admin.PutAsJsonAsync(path, Input()); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("no-store", saved.Headers.CacheControl!.ToString());
        var after = (await admin.GetFromJsonAsync<IssuerProfileView>(path))!; Assert.Equal(Input().Name, after.Name); Assert.Equal(1, after.Version); Assert.False(after.Approved);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path, Input())).StatusCode);
    }

    [PgFact]
    public async Task Invalid_fields_never_write_and_production_ignores_test_profile_and_rejects_edits()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var db = database.Open(); var service = Service(db);
        foreach (var input in new[] { Input() with { Name = " " }, Input() with { Name = "bad\nname" }, Input() with { Address = new string('x', 1001) }, Input() with { Contact = "bad\0contact" }, Input() with { Branch = new string('x', 101) }, Input() with { TaxId = "๑๒๓๔๕๖๗๘๙๐๑๒๓" }, Input() with { ExpectedVersion = -1 } })
            Assert.Equal("invalid_issuer_profile", (await Assert.ThrowsAsync<CommerceException>(() => service.SaveAsync(input, "admin", default))).Code);
        Assert.Equal(0, await db.TestIssuerProfiles.CountAsync());
        await service.SaveAsync(Input() with { Address = new string('x', 1000) }, "admin", default);
        var production = new IssuerProfileService(db, new() { Environment = CommerceEnvironment.Production }, new() { IssuerName = "Approved deployment", CompanyProfileApproved = true }, TimeProvider.System);
        var actual = await production.GetAsync(default); Assert.False(actual.Editable); Assert.True(actual.Approved); Assert.Equal("Approved deployment", actual.Name);
        Assert.Equal("issuer_deployment_managed", (await Assert.ThrowsAsync<CommerceException>(() => production.SaveAsync(Input(1), "admin", default))).Code);
        Assert.Equal(Input().Name, (await service.GetAsync(default)).Name);
    }

    [PgFact]
    public async Task Local_seed_starts_at_one_baht_preserves_edits_and_never_seeds_UAT()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var db = database.Open();
        var seed = new LocalDemoSeeder(db, new(), new(db)); await seed.SeedAsync();
        Assert.Equal(new long[] { 100, 1000, 9900 }, await db.Variants.OrderBy(x => x.PriceMinor).Select(x => x.PriceMinor).ToArrayAsync());
        Assert.True(await db.Products.AnyAsync(x => x.Kind == ProductKind.Physical)); Assert.True(await db.Products.AnyAsync(x => x.Kind == ProductKind.Digital));
        await db.Variants.Where(x => x.PriceMinor == 100).ExecuteUpdateAsync(x => x.SetProperty(v => v.PriceMinor, 200));
        await seed.SeedAsync(); Assert.Equal(3, await db.Products.CountAsync()); Assert.True(await db.Variants.AnyAsync(x => x.PriceMinor == 200));
        await Assert.ThrowsAsync<CommerceException>(() => new LocalDemoSeeder(db, new() { Environment = CommerceEnvironment.Uat, PaymentMode = PaymentMode.Bank }, new(db)).SeedAsync());
    }
}
