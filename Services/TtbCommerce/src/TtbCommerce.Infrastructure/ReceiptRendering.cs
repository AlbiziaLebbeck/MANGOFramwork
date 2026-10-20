using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public interface IReceiptPdfRenderer
{
    Task<byte[]> RenderAsync(ReceiptSnapshot snapshot, CancellationToken cancellationToken);
}

public static class ReceiptTemplate
{
    private static readonly Lazy<string> Font = new(() =>
    {
        using var stream = typeof(ReceiptTemplate).Assembly.GetManifestResourceStream("TtbCommerce.Infrastructure.Fonts.NotoSansThai.ttf")
            ?? throw new InvalidOperationException("The embedded Thai font is missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return Convert.ToBase64String(buffer.ToArray());
    });
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");
    private static string Amount(long value) => (value / 100m).ToString("N2", CultureInfo.InvariantCulture);
    private static string Date(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Numbering.Bangkok).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " (UTC+7)";

    public static string Html(ReceiptSnapshot receipt)
    {
        if (receipt.Void is not null) return VoidHtml(receipt);
        var rows = new StringBuilder(); var index = 0;
        foreach (var line in receipt.Lines)
        {
            if (index == Math.Max(0, receipt.Lines.Length - 2)) rows.Append("</tbody><tbody class='closing'>");
            rows.Append($"<tr><td>{++index}</td><td><strong>{H(line.Name)}</strong><div class='muted'>{H(line.Variant)} · {H(line.Sku)}</div></td><td class='right'>{line.Quantity}</td><td class='right money'>{Amount(line.UnitPriceMinor)}</td><td class='right money'>{Amount(line.TotalMinor)}</td></tr>");
        }
        var test = receipt.Environment != "Production" || receipt.Simulated;
        var banner = test ? $"<div class='test'>{(receipt.Simulated ? "เอกสารจำลอง · ไม่มีการรับเงินจริง" : "UAT · เอกสารทดสอบ ไม่ใช้เป็นหลักฐานรับเงินจริง")}</div>" : "";
        return $$"""
        <!doctype html><html lang="th"><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; font-src data:">
        <title>{{H(receipt.Number)}}</title><style>
        @font-face{font-family:ReceiptThai;src:url(data:font/ttf;base64,{{Font.Value}}) format('truetype');font-weight:100 900;font-style:normal}
        @page{size:A4;margin:17mm 15mm 20mm}*{box-sizing:border-box}body{font-family:ReceiptThai,sans-serif;font-size:10px;line-height:1.65;color:#152722;margin:0;-webkit-print-color-adjust:exact;print-color-adjust:exact}
        h1{font-size:24px;line-height:1.3;margin:0 0 6px;font-weight:650}h2{font-size:13px;margin:0 0 5px}p{margin:3px 0}.header{display:flex;justify-content:space-between;gap:24px;border-bottom:2px solid #243d34;padding-bottom:14px}.brand{font-size:14px;font-weight:700;letter-spacing:2px;margin-bottom:8px}.right{text-align:right}.money{white-space:nowrap}.muted{color:#576b62;font-size:9px}.test{background:#fff4db;border:1px solid #c79c46;color:#704b10;padding:8px 12px;margin-bottom:16px;text-align:center;font-weight:650}
        .details{display:flex;gap:25px;margin:20px 0}.details>div{flex:1;min-width:0}table{width:100%;border-collapse:collapse;table-layout:fixed;margin-top:16px}thead{display:table-header-group}th{font-size:9px;text-align:left;background:#edf2ee;padding:8px 7px}td{vertical-align:top;border-bottom:1px solid #dce4df;padding:9px 7px;overflow-wrap:anywhere}tr{break-inside:avoid}strong{font-weight:600}.totals{width:48%;margin:15px 0 0 auto;break-inside:avoid}.total-row{display:flex;justify-content:space-between;gap:12px;padding:4px 7px}.grand{font-size:14px;font-weight:650;background:#edf2ee;border-top:1px solid #bdcfc2;padding:10px 7px;margin-top:5px}.note{border-top:1px solid #dce4df;margin-top:22px;padding-top:12px;font-size:9px;color:#576b62;break-inside:avoid}.wrap{overflow-wrap:anywhere;white-space:pre-wrap}
        .closing{break-inside:avoid}.summary-cell{border:0;padding:0}.summary-cell .note{white-space:normal}
        </style></head><body>{{banner}}<div class="header"><div><div class="brand">MANGOs</div><h2>{{H(receipt.Issuer.Name)}}</h2><p class="wrap">{{H(receipt.Issuer.Address)}}</p>{{(string.IsNullOrEmpty(receipt.Issuer.TaxId) ? "" : "<p>เลขประจำตัวผู้เสียภาษี " + H(receipt.Issuer.TaxId) + "</p>")}}</div><div class="right"><h1>ใบเสร็จรับเงิน</h1><p>RECEIPT</p><p><strong>{{H(receipt.Number)}}</strong></p><p class="muted">ออกเอกสาร {{Date(receipt.IssuedAt)}}</p></div></div>
        <div class="details"><div><h2>ผู้ซื้อ</h2><div class="wrap">{{H(receipt.Buyer.Name)}}</div><p>{{H(receipt.Buyer.Email)}}</p><p>{{H(receipt.Buyer.Phone)}}</p></div><div><h2>ข้อมูลรายการ</h2><p>เลขที่คำสั่งซื้อ <strong>{{H(receipt.OrderNumber)}}</strong></p><p class="wrap">เลขอ้างอิงรับเงิน {{H(receipt.PaymentReference)}}</p><p>ระบบยืนยันรับเงิน {{Date(receipt.ConfirmedAt)}}</p><p class="muted">เวลาแสดงเป็นเวลายืนยันของระบบ ไม่ใช่เวลาที่ผู้ซื้อโอนจากธนาคาร</p></div></div>
        <table><colgroup><col style="width:5%"><col style="width:45%"><col style="width:10%"><col style="width:19%"><col style="width:21%"></colgroup><thead><tr><th>#</th><th>รายการสินค้า</th><th class="right">จำนวน</th><th class="right">ราคา/หน่วย (บาท)</th><th class="right">จำนวนเงิน (บาท)</th></tr></thead><tbody>{{rows}}<tr><td colspan="5" class="summary-cell">
        <div class="totals"><div class="total-row"><span>รวมค่าสินค้า</span><span class="money">{{Amount(receipt.SubtotalMinor)}}</span></div><div class="total-row"><span>ค่าจัดส่ง</span><span class="money">{{Amount(receipt.ShippingMinor)}}</span></div><div class="total-row grand"><span>ยอดรับชำระ (THB)</span><span class="money">{{Amount(receipt.TotalMinor)}}</span></div></div>
        <div class="note"><p>เอกสารนี้เป็นใบเสร็จรับเงินของผู้ขาย ไม่ใช่สลิปที่ออกโดยธนาคาร และไม่ใช่ใบกำกับภาษี</p><p>การดาวน์โหลดหรือพิมพ์ซ้ำใช้เลขที่และข้อมูลเอกสารเดิม หากรายการถูกยกเลิกภายหลัง ให้ตรวจเอกสารยกเลิกและสถานะในประวัติรายการประกอบ</p>{{(!receipt.Issuer.Approved ? "<p>ข้อมูลผู้ออกเอกสารยังไม่ผ่านการรับรอง ใช้ทดสอบระบบเท่านั้น</p>" : "")}}</div></td></tr></tbody></table></body></html>
        """;
    }

    public static ReceiptSnapshot Read(CommerceDocument document)
    {
        if (!(document.TemplateVersion == "receipt-v1" && document.Kind == "Receipt" || document.TemplateVersion == "void-v1" && document.Kind == "Void")) throw new InvalidOperationException("Unsupported document template.");
        var snapshot = JsonSerializer.Deserialize<ReceiptSnapshot>(document.SnapshotJson, CatalogService.Json) ?? throw new InvalidOperationException("Missing document snapshot.");
        if (snapshot.SchemaVersion != 1 || snapshot.Number != document.Number || (snapshot.Void is null) != (document.Kind == "Receipt") || SecretTokens.Hash(JsonSerializer.Serialize(snapshot, CatalogService.Json)) != document.SnapshotHash)
            throw new InvalidOperationException("Document snapshot integrity check failed.");
        return snapshot;
    }

    private static string VoidHtml(ReceiptSnapshot receipt)
    {
        var info = receipt.Void!; var test = receipt.Environment != "Production" || receipt.Simulated;
        var banner = test ? "<div class='test'>เอกสารทดสอบ · ไม่ใช้เป็นหลักฐานคืนเงินจริง</div>" : "";
        return $$"""
        <!doctype html><html lang="th"><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none';style-src 'unsafe-inline';font-src data:">
        <title>{{H(receipt.Number)}}</title><style>
        @font-face{font-family:ReceiptThai;src:url(data:font/ttf;base64,{{Font.Value}}) format('truetype');font-weight:100 900}
        @page{size:A4;margin:19mm 17mm 21mm}*{box-sizing:border-box}body{font-family:ReceiptThai,sans-serif;font-size:11px;line-height:1.8;color:#152722;margin:0;-webkit-print-color-adjust:exact;print-color-adjust:exact}
        h1{font-size:25px;line-height:1.4;margin:0 0 5px;font-weight:650}h2{font-size:14px;margin:0 0 6px}p{margin:3px 0}.header{display:flex;justify-content:space-between;gap:25px;border-bottom:2px solid #243d34;padding-bottom:17px}.brand{font-size:15px;font-weight:700;letter-spacing:2px;margin-bottom:8px}.right{text-align:right}.muted{color:#576b62;font-size:10px}.wrap{overflow-wrap:anywhere;white-space:pre-wrap}.test{background:#fff4db;border:1px solid #c79c46;color:#704b10;padding:9px 12px;margin-bottom:19px;text-align:center;font-weight:650}.buyer{margin:23px 0}.confirmation{padding:17px;background:#edf2ee;border:1px solid #bdcfc2;margin-bottom:20px;break-inside:avoid}.confirmation strong{font-size:21px}.row{display:flex;gap:22px;padding:9px 0;border-bottom:1px solid #dce4df;break-inside:avoid}.row>span:first-child{flex:0 0 35%;color:#576b62}.row>span:last-child{flex:1;min-width:0}.note{border-top:1px solid #bdcfc2;padding-top:14px;margin-top:25px;font-size:10px;color:#576b62;break-inside:avoid}
        </style></head><body>{{banner}}<div class="header"><div><div class="brand">MANGOs</div><h2>{{H(receipt.Issuer.Name)}}</h2><p class="wrap">{{H(receipt.Issuer.Address)}}</p></div><div class="right"><h1>หลักฐานยกเลิกรายการ</h1><p>VOID CONFIRMATION</p><p><strong>{{H(receipt.Number)}}</strong></p><p class="muted">ออกเอกสาร {{Date(receipt.IssuedAt)}}</p></div></div>
        <div class="buyer"><h2>ผู้ซื้อ</h2><p>{{H(receipt.Buyer.Name)}}</p><p>{{H(receipt.Buyer.Email)}}</p></div>
        <div class="confirmation"><h2>ธนาคารยืนยันว่ารายการรับเงินถูกยกเลิกแล้ว</h2><p>ยอดยกเลิก / คืนเงิน (THB)</p><strong>{{Amount(info.VoidAmountMinor)}} บาท</strong><p class="muted">ระบบตรวจยืนยันผลเมื่อ {{Date(info.ObservedAt)}}</p></div>
        <div class="row"><span>คำสั่งซื้อ</span><span class="wrap">{{H(receipt.OrderNumber)}}</span></div>
        <div class="row"><span>ใบเสร็จเดิม</span><span class="wrap">{{H(info.OriginalReceiptNumber)}}</span></div>
        <div class="row"><span>อ้างอิงรับเงินเดิม</span><span class="wrap">{{H(receipt.PaymentReference)}}</span></div>
        <div class="row"><span>อ้างอิงคำขอยกเลิก</span><span class="wrap">{{H(info.VoidTransactionId ?? "ธนาคารยืนยันผ่านรายงาน ไม่ระบุเลขคำขอ")}}</span></div>
        <div class="row"><span>QR / Ref1 / Ref2</span><span class="wrap">{{H(info.QrId)}}<br>{{H(info.Ref1)}}<br>{{H(info.Ref2)}}</span></div>
        <div class="row"><span>เหตุผล</span><span class="wrap">{{H(info.Reason)}}</span></div>
        <div class="row"><span>ผู้ดำเนินการ</span><span class="wrap">{{H(info.OperatorName ?? "ยืนยันจากรายงานธนาคาร")}}</span></div>
        <div class="note"><p>เอกสารยืนยันของผู้ขาย ไม่ใช่สลิปที่ออกโดยธนาคาร และไม่ใช่ใบกำกับภาษี</p><p>เวลาตรวจยืนยันเป็นเวลาที่ระบบได้รับหลักฐาน ไม่ใช่การระบุเวลาที่เงินถึงบัญชีผู้ชำระ</p><p>เก็บใบเสร็จเดิมและเอกสารนี้ประกอบกัน การพิมพ์ซ้ำใช้เลขที่และข้อมูลเอกสารเดิม</p>{{(!receipt.Issuer.Approved ? "<p>ข้อมูลผู้ออกเอกสารยังไม่ผ่านการรับรอง ใช้ทดสอบระบบเท่านั้น</p>" : "")}}</div></body></html>
        """;
    }
}

public sealed class ChromiumReceiptRenderer(ReceiptOptions options) : IReceiptPdfRenderer
{
    public async Task<byte[]> RenderAsync(ReceiptSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken);
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true,
            ExecutablePath = string.IsNullOrEmpty(options.BrowserExecutablePath) ? null : options.BrowserExecutablePath,
            Args = ["--disable-dev-shm-usage"] }).WaitAsync(cancellationToken);
        await using var context = await browser.NewContextAsync(new() { JavaScriptEnabled = false, Offline = true, ServiceWorkers = ServiceWorkerPolicy.Block });
        await context.RouteAsync("**/*", route => route.AbortAsync());
        var page = await context.NewPageAsync(); page.SetDefaultTimeout(30000);
        await page.SetContentAsync(ReceiptTemplate.Html(snapshot), new() { WaitUntil = WaitUntilState.Load }).WaitAsync(cancellationToken);
        await page.EvaluateAsync("() => document.fonts.ready.then(() => true)").WaitAsync(cancellationToken);
        if (!await page.EvaluateAsync<bool>("() => document.fonts.check('12px ReceiptThai')")) throw new InvalidOperationException("Thai receipt font failed to load.");
        return await page.PdfAsync(new() { Format = "A4", PrintBackground = true, PreferCSSPageSize = true, DisplayHeaderFooter = true,
            HeaderTemplate = "<span></span>", FooterTemplate = "<div style='width:100%;text-align:center;font-family:sans-serif;font-size:8px;color:#68786e'>MANGOs &nbsp; · &nbsp; " + WebUtility.HtmlEncode(snapshot.Number) +
            (snapshot.Environment != "Production" || snapshot.Simulated ? " &nbsp; · &nbsp; TEST" : "") + " &nbsp; · &nbsp; <span class='pageNumber'></span> / <span class='totalPages'></span></div>" }).WaitAsync(cancellationToken);
    }
}

public sealed class DocumentRenderHandler(CommerceDb db, PrivateFiles files, IReceiptPdfRenderer renderer, TimeProvider clock)
{
    public async Task HandleAsync(Guid documentId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var document = await db.Documents.AsNoTracking().SingleAsync(x => x.Id == documentId, cancellationToken);
        if (document.State == "Ready") return;
        var snapshot = ReceiptTemplate.Read(document);
        var pdf = await renderer.RenderAsync(snapshot, cancellationToken);
        if (pdf.Length is < 100 or > 20000000 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new InvalidOperationException("Invalid rendered PDF.");
        var hash = Convert.ToHexStringLower(SHA256.HashData(pdf));
        var key = hash; var path = files.PathFor("documents", key, ".pdf"); var temporary = files.PathFor("documents", Guid.NewGuid().ToString("N"), ".upload");
        try
        {
            await File.WriteAllBytesAsync(temporary, pdf, cancellationToken);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            try { File.Move(temporary, path, overwrite: false); }
            catch (IOException) when (File.Exists(path))
            {
                await using var existing = File.OpenRead(path);
                if (Convert.ToHexStringLower(await SHA256.HashDataAsync(existing, cancellationToken)) != hash) throw new InvalidOperationException("Stored PDF content collision or corruption.");
            }
            // Immutable content-addressed file is durable before the DB points to it. A crash leaves an orphan, never a ready document without a file.
            await db.Documents.Where(x => x.Id == documentId && x.State == "Pending" && x.SnapshotHash == document.SnapshotHash)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Ready").SetProperty(x => x.FileKey, key).SetProperty(x => x.FileSha256, hash)
                    .SetProperty(x => x.FileBytes, (long?)pdf.Length).SetProperty(x => x.ReadyAt, clock.GetUtcNow()), cancellationToken);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class ReceiptWorker(IServiceScopeFactory scopes, CommerceOptions options, TimeProvider clock, ILogger<ReceiptWorker> logger, OperationsOptions? operations = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (operations?.RecoveryFreeze == true || options.Role is not RuntimeRole.All and not RuntimeRole.ReceiptWorker) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope(); var work = scope.ServiceProvider.GetRequiredService<DurableWork>();
                var lease = await work.ClaimAsync(["document.render"], TimeSpan.FromMinutes(3), stoppingToken);
                if (lease is null) { await Task.Delay(TimeSpan.FromSeconds(1), clock, stoppingToken); continue; }
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); timeout.CancelAfter(TimeSpan.FromSeconds(60));
                    using var payload = JsonDocument.Parse(lease.PayloadJson);
                    await scope.ServiceProvider.GetRequiredService<DocumentRenderHandler>().HandleAsync(payload.RootElement.GetProperty("documentId").GetGuid(), timeout.Token);
                    if (!await work.CompleteAsync(lease, stoppingToken)) logger.LogWarning("Receipt lease expired: {JobId}", lease.Id);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    var code = exception is CommerceException commerce ? commerce.Code : exception.GetType().Name;
                    logger.LogWarning("Document job {JobId} failed: {SafeCode}", lease.Id, code);
                    await work.RetryAsync(lease, code, TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(lease.Attempts, 6)))), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning("Receipt worker unavailable: {SafeType}", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
            }
        }
    }
}
