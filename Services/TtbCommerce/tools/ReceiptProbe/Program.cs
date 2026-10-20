using System.Security.Cryptography;
using System.Text.Json;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

// Operator smoke check only: no database, orders, bank calls, credentials or financial records.
if (args.Length is < 1 or > 2 || !Path.IsPathFullyQualified(args[0]) || !args[0].EndsWith(".pdf", StringComparison.Ordinal) || args.Length == 2 && args[1] != "--void")
    throw new ArgumentException("Usage: ReceiptProbe <absolute-output.pdf> [--void]");
var now = DateTimeOffset.UtcNow;
var sample = new ReceiptSnapshot(1, "Local", true, "RENDERER-PROBE", "NO-ORDER",
    new("MANGOs ทดสอบตัวสร้างเอกสาร", "ข้อมูลจำลอง ไม่ใช่เอกสารรับเงิน", "", false), new("ผู้ทดสอบระบบ"),
    [new("สินค้าตัวอย่างสำหรับตรวจฟอนต์ไทย", "ตัวอย่าง", "PROBE", "Digital", 1, 1000, 1000)], "THB", 1000, 0, 1000, "NO-PAYMENT", now, now);
if (args.Length == 2) sample = sample with { Number = "VOID-RENDERER-PROBE", Void = new("RENDERER-PROBE", "NO-QR", "NO-REFERENCE", "NO-REFERENCE", 1000,
    null, "ผู้ทดสอบระบบ", "ตรวจตัวสร้างเอกสารยกเลิกเท่านั้น ไม่มีรายการเงินจริง", "SyntheticProbe", "NO-EVIDENCE", now) };
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var pdf = await new ChromiumReceiptRenderer(new()).RenderAsync(sample, timeout.Token);
if (pdf.Length < 10000) throw new InvalidOperationException("Renderer proof is unexpectedly small.");
await File.WriteAllBytesAsync(args[0], pdf, timeout.Token);
Console.WriteLine(JsonSerializer.Serialize(new { status = "rendered", bytes = pdf.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(pdf)) }));
