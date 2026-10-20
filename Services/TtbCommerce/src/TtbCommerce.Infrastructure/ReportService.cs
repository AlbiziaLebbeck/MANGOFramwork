using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class ReportService(CommerceDb db, CommerceOptions commerce, TtbOptions bank, ReportOptions options, PrivateFiles files,
    IReportDecryptor decryptor, TimeProvider clock)
{
    public async Task<ReportArchive> ImportAsync(string fileName, byte[] encrypted, CancellationToken cancellationToken)
    {
        AssertProfile(); var name = ReportParser.FileName(fileName);
        if (encrypted.Length is < 1 || encrypted.Length > options.MaxFileBytes) throw ReportParser.Error("size");
        var hash = BankWire.Hash(encrypted);
        db.ChangeTracker.Clear();
        Guid id;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockFileAsync(fileName, cancellationToken);
            var existing = await db.Reports.SingleOrDefaultAsync(x => x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId && x.FileName == fileName && x.EncryptedSha256 == hash, cancellationToken);
            if (existing is not null) return existing; // a failed attempt requires an explicit audited reprocess
            var revision = (await db.Reports.Where(x => x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId && x.FileName == fileName).MaxAsync(x => (int?)x.Revision, cancellationToken) ?? 0) + 1;
            await WriteArchiveAsync(hash, encrypted, cancellationToken);
            var archive = new ReportArchive { Environment = commerce.Environment, MerchantId = bank.MerchantId, FileName = name.Name, Kind = name.Kind, FileDate = name.Date,
                Revision = revision, EncryptedSha256 = hash, EncryptedBytes = encrypted.Length, FileKey = hash, ParserProfileJson = ProfileJson(), CreatedAt = clock.GetUtcNow() };
            db.Reports.Add(archive); id = archive.Id;
            db.Jobs.Add(DurableWork.Enqueue($"report-stage:{id}", "report.stage", JsonSerializer.Serialize(new { reportId = id })));
            db.AuditEvents.Add(new AuditEvent { Actor = "report-worker", Action = "report.archive", Resource = $"report:{id}", DataJson = JsonSerializer.Serialize(new { hash, revision, fileName }) });
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        }
        return await db.Reports.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
    }

    public async Task ReprocessAsync(Guid id, string actor, string reason, CancellationToken cancellationToken)
    {
        AssertProfile();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || reason.Any(char.IsControl)) throw new CommerceException(400, "reason_required", "Provide a review reason.");
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var report = await FindLockedAsync(id, cancellationToken);
        if (report.State is "Complete" or "Staged" or "Replay") throw new CommerceException(409, "report_already_staged", "Existing immutable rows are already staged or complete.");
        // Every reprocess keeps the original archive/profile and appends its new profile/reason to the audit trail.
        db.AuditEvents.Add(new AuditEvent { Actor = actor, Action = "report.reprocess", Resource = $"report:{id}", DataJson = JsonSerializer.Serialize(new { reason, originalProfile = report.ParserProfileJson,
            newProfile = ProfileJson(ArchivedCryptoProfile(report.ParserProfileJson)), report.State }) });
        report.State = "Archived"; report.FailureCode = null;
        var operation = Guid.CreateVersion7();
        db.Jobs.Add(DurableWork.Enqueue($"report-stage:{id}:{operation}", "report.stage", JsonSerializer.Serialize(new { reportId = id, revisionApproved = true })));
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task StageAsync(Guid id, bool revisionApproved, CancellationToken cancellationToken)
    {
        AssertProfile(); db.ChangeTracker.Clear();
        var archive = await db.Reports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId, cancellationToken) ?? throw Missing();
        if (archive.State != "Archived") return;
        ParsedReport parsed; string plainHash; ReportCryptoProfile? crypto;
        try
        {
            var path = files.PathFor("reports", archive.FileKey, ".pgp");
            if (!File.Exists(path) || new FileInfo(path).Length != archive.EncryptedBytes) throw ReportParser.Error("archive_corrupt");
            var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
            if (BankWire.Hash(encrypted) != archive.EncryptedSha256) throw ReportParser.Error("archive_corrupt");
            crypto = ArchivedCryptoProfile(archive.ParserProfileJson);
            var plain = decryptor.Decrypt(encrypted, crypto); plainHash = BankWire.Hash(plain);
            try { parsed = ReportParser.Parse(plain, archive.FileName, options); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); }
            if (parsed.MerchantId != bank.MerchantId || parsed.Details.Any(x => x.BillerNo != bank.BillerNo || x.MerchantAccount != options.MerchantAccount)) throw ReportParser.Error("beneficiary");
        }
        catch (CommerceException exception) { await QuarantineAsync(id, exception.Code, cancellationToken); return; }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockFileAsync(archive.FileName, cancellationToken);
        var current = await FindLockedAsync(id, cancellationToken);
        if (current.State != "Archived") return;
        current.PlaintextSha256 = plainHash;
        var replay = await db.Reports.AsNoTracking().Where(x => x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId && x.FileName == current.FileName &&
            x.Id != id && x.PlaintextSha256 == plainHash && (x.State == "Staged" || x.State == "Complete" || x.State == "Replay")).OrderBy(x => x.Revision).FirstOrDefaultAsync(cancellationToken);
        if (replay is not null)
        { current.ReplayOfId = replay.ReplayOfId ?? replay.Id; current.State = "Replay"; current.CompletedAt = clock.GetUtcNow(); }
        else if (current.Revision > 1 && !revisionApproved)
        {
            current.State = "RevisionReview"; current.FailureCode = "report_content_revision";
            await CaseAsync($"report-revision:{id}", "ReportContentRevision", null, null, new { reportId = id, current.Revision }, cancellationToken);
        }
        else
        {
            // No rows or jobs are staged until the complete file, beneficiary and totals have passed.
            current.AppliedProfileJson = ProfileJson(crypto);
            foreach (var detail in parsed.Details)
            {
                var row = new ReportRow { ReportArchiveId = id, Sequence = detail.Sequence, PayloadJson = JsonSerializer.Serialize(detail, CatalogService.Json), SemanticHash = SemanticHash(detail) };
                db.ReportRows.Add(row);
                db.Jobs.Add(DurableWork.Enqueue($"report-row:{row.Id}", "report.row", JsonSerializer.Serialize(new { rowId = row.Id })));
            }
            current.TotalsJson = JsonSerializer.Serialize(parsed.Totals, CatalogService.Json);
            current.State = parsed.Details.Length == 0 ? "Complete" : "Staged";
            if (parsed.Details.Length == 0) { current.CompletedAt = clock.GetUtcNow(); QueueComparison(id); }
        }
        db.AuditEvents.Add(new AuditEvent { Actor = "report-worker", Action = "report.stage", Resource = $"report:{id}", DataJson = JsonSerializer.Serialize(new { profile = ProfileJson(crypto), plainHash, current.State }) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReconcileAsync(Guid id, CancellationToken cancellationToken)
    {
        AssertProfile(); db.ChangeTracker.Clear();
        var lookup = await db.ReportRows.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
        var archive = await db.Reports.AsNoTracking().SingleAsync(x => x.Id == lookup.ReportArchiveId && x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId, cancellationToken);
        if (archive.State is not ("Staged" or "Complete")) return;
        var detail = JsonSerializer.Deserialize<ReportDetail>(lookup.PayloadJson, CatalogService.Json) ?? throw ReportParser.Error("row_corrupt");
        if (SemanticHash(detail) != lookup.SemanticHash || detail.BillerNo != bank.BillerNo || detail.MerchantAccount != options.MerchantAccount) throw ReportParser.Error("row_corrupt");
        var originalId = detail.TransactionKind == "D" ? detail.OriginalInstructionId : detail.InstructionId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Same instruction lock as callback/Verify, followed by order then bank/row locks.
        var instructionLock = $"bank:{commerce.Environment}:{bank.MerchantId}:{originalId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({instructionLock}, 0))", cancellationToken);
        var payment = await db.BankTransactions.SingleOrDefaultAsync(x => x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId && x.BillerNo == detail.BillerNo && x.InstructionId == originalId, cancellationToken);
        var attempt = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(x => x.MerchantId == bank.MerchantId && x.QrId == detail.QrId, cancellationToken);
        var allocationOrder = payment is null ? null : await db.Allocations.Where(x => x.BankTransactionId == payment.Id && x.Primary).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(cancellationToken);
        var orderId = allocationOrder ?? attempt?.OrderId;
        Order? order = null;
        if (orderId is not null) order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId.Value} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        var row = (await db.ReportRows.FromSqlInterpolated($"SELECT * FROM ttb.\"ReportRows\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        if (row.AppliedAt is not null) return;
        string outcome;
        if (payment is null)
        {
            // Report IDs are35 columns while API IDs may be50. Never create a second payment by truncating/prefix-matching an API identifier.
            var prefixExists = await db.BankTransactions.AnyAsync(x => x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId && x.InstructionId.StartsWith(originalId) && x.InstructionId != originalId, cancellationToken);
            outcome = prefixExists ? "ReportInstructionWidthMismatch" : detail.TransactionKind == "C" ? "ReportUnallocatedCredit" : "ReportUnknownVoid";
        }
        else if (payment.QrId != detail.QrId || payment.Ref1 != detail.Ref1 || payment.Ref2.Length > 0 && payment.Ref2 != detail.Ref2 ||
            payment.AmountMinor != detail.AmountMinor || (payment.CrossBorderFlag == "Y") != (detail.CrossBorderFlag == "Y") ||
            (payment.SwitchCode ?? "") != detail.SwitchCode || (payment.CurrencyCode ?? "") != detail.CurrencyCode)
            outcome = "ReportPaymentMismatch";
        else if (detail.TransactionKind == "C")
        {
            if (payment.Voided) outcome = "ReportCreditAfterVoid";
            else if (archive.Kind == ReportKind.Iso && payment.PaymentOccurredAt is { } at && at != detail.PaymentAt) outcome = "ReportPaymentTimeConflict";
            else
            {
                // LOST payer debit and operation credit timestamps are separate row evidence, not the original credited-payment time.
                if (archive.Kind == ReportKind.Iso && payment.PaymentOccurredAt is null)
                { payment.PaymentOccurredAt = detail.PaymentAt.ToUniversalTime(); payment.PaymentTimeSource = "ReportIsoPayment"; }
                if (payment.Ref2.Length == 0) payment.Ref2 = detail.Ref2;
                outcome = allocationOrder is null ? "ReportUnallocatedCredit" : "MatchedCredit";
            }
        }
        else
        {
            outcome = detail.TransactionKind == "D" ? "MatchedVoidDebit" : "MatchedVoidedOriginal";
            await VoidLedger.ConfirmAsync(db, payment, order, "Report" + detail.TransactionKind, $"report-row:{id}", clock, cancellationToken);
            if (allocationOrder is not null && order is not null)
            {
                await CaseAsync($"reported-void:{payment.Id}", "ReportedVoidFulfillmentReview", order.Id, payment.Id, new { rowId = id, reportId = archive.Id }, cancellationToken);
            }
        }
        row.BankTransactionId = payment?.Id; row.Outcome = outcome; row.AppliedAt = clock.GetUtcNow();
        if (!outcome.StartsWith("Matched", StringComparison.Ordinal))
        {
            await CaseAsync($"report-row:{id}:{outcome}", outcome, orderId, payment?.Id, new { rowId = id, reportId = archive.Id }, cancellationToken);
            if (order is not null) order.State = OrderState.Review;
        }
        db.AuditEvents.Add(new AuditEvent { Actor = "report-worker", Action = "report.reconcile", Resource = $"report-row:{id}", DataJson = JsonSerializer.Serialize(new { outcome, bankTransactionId = payment?.Id, orderId }) });
        await db.SaveChangesAsync(cancellationToken);
        // Serialize the completion count only after money/order locks; concurrent rows cannot leave the file perpetually Staged.
        var current = await FindLockedAsync(archive.Id, cancellationToken);
        if (!await db.ReportRows.AnyAsync(x => x.ReportArchiveId == archive.Id && x.AppliedAt == null, cancellationToken))
        { current.State = "Complete"; current.CompletedAt = clock.GetUtcNow(); QueueComparison(archive.Id); }
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    private async Task QuarantineAsync(Guid id, string code, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var archive = await FindLockedAsync(id, cancellationToken);
        if (archive.State != "Archived") return;
        archive.State = "Quarantined"; archive.FailureCode = code;
        await CaseAsync($"report-invalid:{id}:{code}", "ReportQuarantined", null, null, new { reportId = id, code }, cancellationToken);
        db.AuditEvents.Add(new AuditEvent { Actor = "report-worker", Action = "report.quarantine", Resource = $"report:{id}", DataJson = JsonSerializer.Serialize(new { code, profile = ProfileJson() }) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }
    private async Task WriteArchiveAsync(string hash, byte[] bytes, CancellationToken cancellationToken)
    {
        var path = files.PathFor("reports", hash, ".pgp");
        var temporary = files.PathFor("reports", Guid.NewGuid().ToString("N"), ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            try { File.Move(temporary, path, overwrite: false); } catch (IOException) when (File.Exists(path)) { }
            await using var existing = File.OpenRead(path);
            if (existing.Length != bytes.Length || Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(existing, cancellationToken)) != hash) throw ReportParser.Error("archive_corrupt");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task CaseAsync(string key, string kind, Guid? orderId, Guid? bankId, object evidence, CancellationToken cancellationToken)
    {
        if (!await db.Cases.AnyAsync(x => x.EffectKey == key, cancellationToken)) db.Cases.Add(new ReconciliationCase { EffectKey = key, Kind = kind, OrderId = orderId, BankTransactionId = bankId, EvidenceJson = JsonSerializer.Serialize(evidence), CreatedAt = clock.GetUtcNow() });
    }
    private Task<int> LockFileAsync(string fileName, CancellationToken cancellationToken)
    {
        var key = $"report-file:{commerce.Environment}:{bank.MerchantId}:{fileName}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
    private async Task<ReportArchive> FindLockedAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Reports.FromSqlInterpolated($"SELECT * FROM ttb.\"Reports\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault(x => x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId) ?? throw Missing();
    private void AssertProfile()
    {
        if (commerce.Environment == CommerceEnvironment.Local || commerce.PaymentMode != PaymentMode.Bank || options.MissingParser().Count > 0 ||
            bank.MerchantId.Length != 3 || bank.BillerNo.Length != 15 || options.MerchantAccount.Length == 0) throw ReportParser.Error("profile_unapproved");
    }
    private void QueueComparison(Guid id) => db.Jobs.Add(DurableWork.Enqueue($"report-compare:{id}", "report.compare", JsonSerializer.Serialize(new { reportId = id })));
    private string ProfileJson(ReportCryptoProfile? crypto = null) => JsonSerializer.Serialize(new { options.Layout, options.Width, options.CreditTotal, options.DailyScope,
        options.SequenceIncludesTrailer, options.FileDateIsHeaderDate, crypto = crypto ?? new(options.RecipientFingerprint, options.SigningFingerprint) }, CatalogService.Json);
    private static ReportCryptoProfile? ArchivedCryptoProfile(string profileJson)
    {
        try
        {
            using var json = JsonDocument.Parse(profileJson);
            // Older archives have no crypto snapshot: current pins only, with no historical-key guessing.
            if (!json.RootElement.TryGetProperty("crypto", out var crypto)) return null;
            if (crypto.ValueKind != JsonValueKind.Object || !crypto.TryGetProperty("recipientFingerprint", out var recipient) || recipient.ValueKind != JsonValueKind.String ||
                !crypto.TryGetProperty("signingFingerprint", out var signing) || signing.ValueKind != JsonValueKind.String) throw ReportParser.Error("pgp_archive_profile");
            return new(recipient.GetString()!, signing.GetString()!);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { throw ReportParser.Error("pgp_archive_profile"); }
    }
    private static string SemanticHash(ReportDetail row) => SecretTokens.Hash(JsonSerializer.Serialize(row, CatalogService.Json));
    private static CommerceException Missing() => new(404, "not_found", "The bank report was not found.");
}

public sealed class ReportStageHandler(ReportService reports) : ICommerceJobHandler
{
    public string Kind => "report.stage";
    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(payloadJson);
        await reports.StageAsync(json.RootElement.GetProperty("reportId").GetGuid(), json.RootElement.TryGetProperty("revisionApproved", out var approved) && approved.GetBoolean(), cancellationToken);
    }
}
public sealed class ReportRowHandler(ReportService reports) : ICommerceJobHandler
{
    public string Kind => "report.row";
    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(payloadJson); await reports.ReconcileAsync(json.RootElement.GetProperty("rowId").GetGuid(), cancellationToken);
    }
}
