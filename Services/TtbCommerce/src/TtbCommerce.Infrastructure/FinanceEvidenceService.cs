using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class FinanceEvidenceService(CommerceDb db, CommerceOptions commerce, TtbOptions ttb, ManualRefundOptions policy, PrivateFiles files, TimeProvider clock)
{
    private string Merchant => commerce.PaymentMode == PaymentMode.Mock ? "SIM" : ttb.MerchantId;
    private string Biller => commerce.PaymentMode == PaymentMode.Mock ? "000000000000000" : ttb.BillerNo;
    public async Task<FinanceEvidenceView> UploadAsync(Guid caseId, Stream input, string fileName, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor);
        if (policy.MaxEvidenceBytes is <= 0 or > 20 * 1024 * 1024) throw new CommerceException(503, "finance_evidence_unavailable", "Evidence storage bounds are not configured safely.");
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 180 || fileName.Any(char.IsControl) || fileName.Contains('/') || fileName.Contains('\\') ||
            Path.GetExtension(fileName).ToLowerInvariant() is not (".pdf" or ".png" or ".jpg" or ".jpeg" or ".txt"))
            throw new CommerceException(400, "finance_evidence_file_invalid", "Use a PDF, PNG, JPEG or UTF8 TXT evidence file.");
        if (!await OwnCaseAsync(caseId).AnyAsync(cancellationToken)) throw Missing();
        using var buffer = new MemoryStream(); var chunk = new byte[81920]; int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > policy.MaxEvidenceBytes) throw new CommerceException(413, "finance_evidence_too_large", "Evidence files are limited to20MiB.");
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray(); ValidateBytes(fileName, bytes); var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var review = (await db.Cases.FromSqlInterpolated($"SELECT * FROM ttb.\"Cases\" WHERE \"Id\" = {caseId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault() ?? throw Missing();
        var manual = await OwnCaseAsync(caseId).SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
        var existing = await db.FinanceEvidence.AsNoTracking().SingleOrDefaultAsync(x => x.CaseId == caseId && x.Sha256 == hash, cancellationToken);
        if (existing is not null) return View(existing);
        if (review.State != "Open" || manual.State != "Review") throw new CommerceException(409, "finance_evidence_case_closed", "New files require a manual case currently under review.");
        if (await db.FinanceEvidence.CountAsync(x => x.CaseId == caseId, cancellationToken) >= 25) throw new CommerceException(409, "finance_evidence_limit", "This case already has25 evidence files.");
        var key = Guid.NewGuid().ToString("N"); var temporary = files.PathFor("finance", key, ".upload"); var final = files.PathFor("finance", key, ".proof");
        var databaseWriteStarted = false;
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken); File.Move(temporary, final, overwrite: false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(final, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            var row = new FinanceEvidenceFile { CaseId = caseId, Key = key, FileName = fileName, ByteLength = bytes.Length, Sha256 = hash, UploadedBy = actor.Key, CreatedAt = clock.GetUtcNow() };
            db.FinanceEvidence.Add(row); review.Version++; manual.Version++;
            db.CaseNotes.Add(new FinanceCaseNote { CaseId = caseId, Actor = actor.Key, Body = "แนบหลักฐานส่วนตัว: " + fileName, CreatedAt = clock.GetUtcNow() });
            db.AuditEvents.Add(new AuditEvent { Actor = actor.Key, Action = "finance.evidence.upload", Resource = $"finance-evidence:{row.Id}", DataJson = JsonSerializer.Serialize(new { caseId, row.Sha256, row.ByteLength }), CreatedAt = clock.GetUtcNow() });
            databaseWriteStarted = true; await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return View(row);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            // An ambiguous DB acknowledgement must not delete bytes which a committed record could reference.
            if (!databaseWriteStarted && File.Exists(final)) File.Delete(final);
        }
    }

    public async Task<DocumentDownload> DownloadAsync(Guid id, ShopperActor actor, string correlationId, CancellationToken cancellationToken)
    {
        Finance(actor);
        var row = await (from evidence in db.FinanceEvidence.AsNoTracking() join manual in db.ManualRefunds.AsNoTracking() on evidence.CaseId equals manual.CaseId
                         where evidence.Id == id && manual.Environment == commerce.Environment && manual.MerchantId == Merchant && manual.BillerNo == Biller select evidence).SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
        FileStream stream;
        try { stream = new FileStream(files.PathFor("finance", row.Key, ".proof"), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous); }
        catch (IOException) { throw Unavailable(); }
        try
        {
            if (stream.Length != row.ByteLength || Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken)) != row.Sha256) throw Unavailable();
            stream.Position = 0;
            db.AuditEvents.Add(new AuditEvent { Actor = actor.Key, Action = "finance.evidence.download-request", Resource = $"finance-evidence:{id}", CorrelationId = correlationId, CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken); return new(stream, row.FileName);
        }
        catch { await stream.DisposeAsync(); throw; }
    }
    private IQueryable<ManualRefund> OwnCaseAsync(Guid caseId) => db.ManualRefunds.Where(x => x.CaseId == caseId && x.Environment == commerce.Environment && x.MerchantId == Merchant && x.BillerNo == Biller);
    private static void ValidateBytes(string fileName, byte[] bytes)
    {
        var valid = bytes.Length > 0;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".pdf") valid &= bytes.AsSpan().StartsWith("%PDF-"u8);
        else if (extension == ".png") valid &= bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        else if (extension is ".jpg" or ".jpeg") valid &= bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        else
        {
            try { valid &= !new UTF8Encoding(false, true).GetString(bytes).Contains('\0'); }
            catch (DecoderFallbackException) { valid = false; }
        }
        if (!valid) throw new CommerceException(400, "finance_evidence_file_invalid", "The file bytes do not match the declared evidence type.");
    }
    private static FinanceEvidenceView View(FinanceEvidenceFile row) => new(row.Id, row.FileName, row.ByteLength, row.Sha256, row.UploadedBy, row.CreatedAt);
    private static void Finance(ShopperActor actor) { if (!actor.HasRole("finance")) throw new CommerceException(403, "role_required", "Finance permission is required."); }
    private static CommerceException Missing() => new(404, "not_found", "The private evidence or manual case was not found.");
    private static CommerceException Unavailable() => new(503, "finance_evidence_unavailable", "The private evidence bytes are temporarily unavailable.");
}
