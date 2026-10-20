using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class ReceiptTests
{
    private static ReceiptService Service(CommerceDb db, TestPrivateStorage storage, ReceiptOptions? options = null) =>
        new(db, new(db, TimeProvider.System), new(), options ?? new(), TimeProvider.System, storage.Files);
    private static async Task<PaymentFixture> PaidAsync(PgDatabase database)
    {
        var fixture = await PaymentFixture.CreateAsync(database); await using var db = database.Open();
        await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default); return fixture;
    }

    internal sealed class FakeRenderer(bool fail = false) : IReceiptPdfRenderer
    {
        public int Calls;
        public Task<byte[]> RenderAsync(ReceiptSnapshot snapshot, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (fail) throw new InvalidOperationException("Simulated renderer failure");
            return Task.FromResult(Encoding.UTF8.GetBytes("%PDF-1.7\n" + new string('x', 150) + "\n%%EOF"));
        }
    }

    [PgFact]
    public async Task Unpaid_cannot_issue_and_concurrent_paid_retries_keep_one_number_and_immutable_snapshot()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await PaymentFixture.CreateAsync(database);
        await using (var db = database.Open()) Assert.Equal("receipt_not_ready", (await Assert.ThrowsAsync<CommerceException>(() => Service(db, storage).EnsureAsync(fixture.OrderId, default))).Code);
        await using (var db = database.Open()) await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); return await Service(db, storage).EnsureAsync(fixture.OrderId, default); }));
        Assert.Single(results.Select(x => x.Id).Distinct()); Assert.Single(results.Select(x => x.Number).Distinct());
        await using var check = database.Open(); var original = await check.Documents.AsNoTracking().SingleAsync();
        Assert.Equal("Pending", original.State); Assert.Equal(1, await check.Jobs.CountAsync(x => x.Kind == "document.render"));
        await check.OrderLines.Where(x => x.OrderId == fixture.OrderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "ชื่อปัจจุบันที่เปลี่ยนแล้ว"));
        await Service(check, storage, new() { IssuerName = "บริษัทใหม่" }).EnsureAsync(fixture.OrderId, default);
        var after = await check.Documents.AsNoTracking().SingleAsync(); Assert.Equal(original.SnapshotJson, after.SnapshotJson); Assert.Equal(original.SnapshotHash, after.SnapshotHash);
        var snapshot = ReceiptTemplate.Read(after); Assert.NotEqual("บริษัทใหม่", snapshot.Issuer.Name); Assert.DoesNotContain(snapshot.Lines, x => x.Name == "ชื่อปัจจุบันที่เปลี่ยนแล้ว");
        Assert.Equal(snapshot.SubtotalMinor + snapshot.ShippingMinor, snapshot.TotalMinor); Assert.Equal(1, await check.AuditEvents.CountAsync(x => x.Action == "receipt.issue"));
    }

    [PgFact]
    public async Task Rendering_failure_and_retries_keep_number_and_ready_pdf_bytes_stable()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await PaidAsync(database);
        await using var db = database.Open(); var document = await Service(db, storage).EnsureAsync(fixture.OrderId, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DocumentRenderHandler(db, storage.Files, new FakeRenderer(true), TimeProvider.System).HandleAsync(document.Id, default));
        Assert.Equal("Pending", (await db.Documents.AsNoTracking().SingleAsync()).State);
        var renderer = new FakeRenderer(); var handler = new DocumentRenderHandler(db, storage.Files, renderer, TimeProvider.System);
        await handler.HandleAsync(document.Id, default); await handler.HandleAsync(document.Id, default); Assert.Equal(1, renderer.Calls);
        var ready = await db.Documents.AsNoTracking().SingleAsync(); Assert.Equal(document.Number, ready.Number); Assert.Equal("Ready", ready.State);
        var first = await Service(db, storage).DownloadAsync(fixture.ShopperId, document.Id, "owner", "first", default);
        await using var firstStream = first.Stream; using var bytes = new MemoryStream(); await firstStream.CopyToAsync(bytes);
        var second = await Service(db, storage).DownloadAsync(fixture.ShopperId, document.Id, "owner", "second", default);
        await using var secondStream = second.Stream; using var again = new MemoryStream(); await secondStream.CopyToAsync(again); Assert.Equal(bytes.ToArray(), again.ToArray());
        Assert.Equal(2, await db.AuditEvents.CountAsync(x => x.Action == "document.download-request"));
    }

    [PgFact]
    public async Task Document_snapshot_corruption_never_renders_or_becomes_ready()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await PaidAsync(database);
        await using var db = database.Open(); var document = await Service(db, storage).EnsureAsync(fixture.OrderId, default);
        await db.Documents.Where(x => x.Id == document.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SnapshotHash, new string('0', 64)));
        var renderer = new FakeRenderer();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DocumentRenderHandler(db, storage.Files, renderer, TimeProvider.System).HandleAsync(document.Id, default));
        Assert.Equal(0, renderer.Calls); Assert.Equal("Pending", (await db.Documents.AsNoTracking().SingleAsync()).State);
    }

    [PgFact]
    public async Task HTTP_documents_and_activity_are_owned_private_and_finance_downloads_require_role()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await PaidAsync(database); DocumentView document;
        await using (var db = database.Open())
        {
            await db.Shoppers.Where(x => x.Id == fixture.ShopperId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Issuer, "mangosgo").SetProperty(x => x.Subject, "alice"));
            document = await Service(db, storage).EnsureAsync(fixture.OrderId, default);
        }
        await using var factory = new CommerceApiFactory(database.ConnectionString) { PrivateRoot = storage.Root };
        using var alice = factory.ShopperClient("alice"); using var bob = factory.ShopperClient("bob"); using var admin = factory.ShopperClient("admin");
        await CartTests.SignInAsync(alice); await CartTests.SignInAsync(bob); await CartTests.SignInAsync(admin);
        var path = $"/ttb-commerce/v1/documents/{document.Id}/download";
        Assert.Equal(HttpStatusCode.Conflict, (await alice.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/ttb-commerce/v1/orders/{fixture.OrderId}/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/ttb-commerce/v1/admin/documents/{document.Id}/download")).StatusCode);
        await using (var db = database.Open()) await new DocumentRenderHandler(db, storage.Files, new FakeRenderer(), TimeProvider.System).HandleAsync(document.Id, default);
        var response = await alice.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore); Assert.Contains(document.Number, response.Content.Headers.ContentDisposition?.ToString() ?? "");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/ttb-commerce/v1/admin/documents/{document.Id}/download")).StatusCode);
        var activity = await alice.GetFromJsonAsync<OrderActivity>($"/ttb-commerce/v1/orders/{fixture.OrderId}/activity");
        Assert.Equal("Ready", Assert.Single(activity!.Documents).State); Assert.Equal(fixture.Notice.InstructionId, activity.Payment?.Reference); Assert.False(activity.Payment?.OriginalBankPaymentTimeKnown);
        await using (var db = database.Open())
        {
            await db.BankTransactions.ExecuteUpdateAsync(s => s.SetProperty(x => x.Voided, true));
            // Original receipt remains evidence of the allocated credit after a later void.
            Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync(path)).StatusCode);
            var row = await db.Documents.SingleAsync(); await File.WriteAllTextAsync(storage.Files.PathFor("documents", row.FileKey!, ".pdf"), "corrupt");
        }
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await alice.GetAsync(path)).StatusCode);
    }

    [PgFact]
    public async Task Concurrent_distinct_paid_orders_receive_unique_receipt_numbers()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage();
        var first = await PaidAsync(database); var second = await PaidAsync(database);
        var views = await Task.WhenAll(new[] { first, second }.Select(async f => { await using var db = database.Open(); return await Service(db, storage).EnsureAsync(f.OrderId, default); }));
        Assert.Equal(2, views.Select(x => x.Number).Distinct().Count());
    }

    internal static ReceiptSnapshot Sample(int lines = 1) => new(1, "Local", true, "LR20261018000001", "L20261018000001",
        new("บริษัท ตัวอย่างเพื่อทดสอบ จำกัด", "99 ถนนตัวอย่าง แขวงปทุมวัน เขตปทุมวัน กรุงเทพมหานคร 10330", "", false),
        new("คุณสมชาย ใจดี", "buyer@example.test", "0812345678"), Enumerable.Range(1, lines).Select(i => new ReceiptLine("โคมไฟวงโคจร รุ่นพิเศษสำหรับพื้นที่สร้างสรรค์ " + i, "สีเขียวมะกอก · สินค้าตัวอย่าง", "DEMO-" + i, "Physical", 2, 125050, 250100)).ToArray(),
        "THB", 250100L * lines, 5000, 250100L * lines + 5000, "MOCK-REFERENCE-ONLY", DateTimeOffset.Parse("2026-10-18T10:00:00+07:00", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-10-18T09:59:00+07:00", System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Receipt_template_escapes_all_buyer_catalog_and_issuer_text_and_embeds_font()
    {
        var snapshot = Sample() with { Buyer = new("<script>fetch('https://evil.test')</script>"), Issuer = new("<img src=x>", "&address", "", false) };
        var html = ReceiptTemplate.Html(snapshot);
        Assert.DoesNotContain("<script>", html); Assert.DoesNotContain("<img src=x>", html); Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("data:font/ttf;base64,", html); Assert.Contains("เอกสารจำลอง", html); Assert.Contains("ยอดรับชำระ", html);
    }

    [Fact]
    public void Production_requires_approved_issuer_and_does_not_claim_a_tax_invoice()
    {
        Assert.Throws<InvalidOperationException>(() => new ReceiptOptions().AssertProductionReady(CommerceEnvironment.Production));
        Assert.Throws<InvalidOperationException>(() => new ReceiptOptions { CompanyProfileApproved = true }.AssertProductionReady(CommerceEnvironment.Production));
        new ReceiptOptions { CompanyProfileApproved = true, IssuerName = "บริษัททดสอบ", IssuerAddress = "99 กรุงเทพ", TaxId = "1234567890123" }.AssertProductionReady(CommerceEnvironment.Production);
        var html = ReceiptTemplate.Html(Sample() with { Environment = "Production", Simulated = false, Issuer = new("บริษัททดสอบ", "99 กรุงเทพ", "1234567890123", true) });
        Assert.DoesNotContain("เอกสารจำลอง", html); Assert.Contains("ไม่ใช่ใบกำกับภาษี", html);
    }

    [Fact]
    public async Task Actual_chromium_generates_Thai_receipt_with_many_lines_for_visual_QA()
    {
        var bytes = await new ChromiumReceiptRenderer(new()).RenderAsync(Sample(30), default);
        Assert.True(bytes.Length > 10000); Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        var root = Directory.GetCurrentDirectory();
        while (!Directory.Exists(Path.Combine(root, "Documentation"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace root missing.");
        var output = Path.Combine(root, ".artifacts", "pdf-qa"); Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, "receipt-thai-multipage.pdf"), bytes);
    }
}
