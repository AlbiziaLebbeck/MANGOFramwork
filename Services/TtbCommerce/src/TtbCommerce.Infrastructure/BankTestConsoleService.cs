using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QRCoder;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed partial class BankTestConsoleService(
    CommerceDb db, CommerceOptions commerce, TtbOptions ttb, BankTestOptions options, Numbering numbering,
    BankReadiness readiness, IBankGateway gateway, TimeProvider clock, ILogger<BankTestConsoleService> logger)
{
    public void Guard()
    {
        if (commerce.Environment != CommerceEnvironment.Uat || commerce.PaymentMode != PaymentMode.Bank || !options.Enabled)
            throw new CommerceException(404, "bank_test_unavailable", "The bank test console is available only in enabled UAT.");
        if (!options.ValidProfile()) throw new CommerceException(503, "bank_test_profile_invalid", "The bank test profile needs operator review.");
    }

    public async Task<BankTestCaseView> CreateAsync(BankTestCreateInput input, CancellationToken ct)
    {
        Guard(); readiness.AssertOutgoing();
        if (!ttb.QrProfileApproved) throw new CommerceException(503, "bank_test_qr_unapproved", "The UAT QR profile is not approved.");
        long amount;
        try { amount = Money.Parse(input.Amount).Minor; }
        catch (FormatException) { throw new CommerceException(400, "amount_invalid", "Enter a positive THB amount with at most two decimals."); }
        if (amount < 1 || amount > options.MaximumAmountMinor)
            throw new CommerceException(400, "bank_test_amount_limit", "The amount is outside the configured UAT test limit.");
        var now = clock.GetUtcNow(); var expiry = DateTimeOffset.FromUnixTimeSeconds(now.AddMinutes(commerce.PaymentMinutes).ToUnixTimeSeconds() / 60 * 60);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var local = TimeZoneInfo.ConvertTime(expiry, Numbering.Bangkok);
        var sequence = await numbering.NextAsync($"qr:{ttb.MerchantId}:{local:yyyyMMddHHmm}", 99999, ct);
        var row = new BankTestCase { MerchantId = ttb.MerchantId, BillerNo = ttb.BillerNo, Ref1 = options.Reference1,
            Ref2 = "BT" + Guid.NewGuid().ToString("N")[..18].ToUpperInvariant(), AmountMinor = amount, ExpiresAt = expiry, CreatedAt = now,
            QrId = string.Create(CultureInfo.InvariantCulture, $"Z{ttb.MerchantId}E{local:yyMMddHHmm}{sequence:D5}") };
        row.Payload = ThaiQr.Build(new(row.BillerNo, row.Ref1, row.Ref2, row.QrId, new Money(amount), ttb.QrMerchantName));
        db.BankTestCases.Add(row);
        var entry = new BankTestEvent { CaseId = row.Id, Kind = "QrCreated", Outcome = "AwaitingPayment", Ref1 = row.Ref1,
            Ref2 = row.Ref2, QrId = row.QrId, AmountMinor = amount, CreatedAt = now,
            PayloadHash = BankWire.Hash(System.Text.Encoding.UTF8.GetBytes(row.Payload)),
            PayloadJson = JsonSerializer.Serialize(new { row.Ref1, row.Ref2, row.QrId, row.AmountMinor, row.ExpiresAt }) };
        db.BankTestEvents.Add(entry); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); Log(entry);
        return View(row, 0, image: true);
    }

    public async Task<BankTestCaseView[]> CasesAsync(int limit, CancellationToken ct)
    {
        Guard(); var rows = await db.BankTestCases.AsNoTracking().OrderByDescending(x => x.Id).Take(Math.Clamp(limit, 1, 100)).ToArrayAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var counts = await db.BankTestOperations.Where(x => ids.Contains(x.CaseId) && x.Kind == "verify")
            .GroupBy(x => x.CaseId).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return rows.Select(x => View(x, counts.GetValueOrDefault(x.Id))).ToArray();
    }

    public async Task<BankTestCaseView> CaseAsync(Guid id, CancellationToken ct)
    {
        Guard(); var row = await db.BankTestCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        return View(row, await db.BankTestOperations.CountAsync(x => x.CaseId == id && x.Kind == "verify", ct), image: true);
    }

    public async Task<BankTestEventView[]> EventsAsync(Guid? caseId, string? kind, Guid? before, int limit, CancellationToken ct)
    {
        Guard(); var query = db.BankTestEvents.AsNoTracking().AsQueryable();
        if (caseId is not null) query = query.Where(x => x.CaseId == caseId);
        if (kind is not null) query = query.Where(x => x.Kind == kind);
        if (before is not null) query = query.Where(x => x.Id.CompareTo(before.Value) < 0);
        return (await query.OrderByDescending(x => x.Id).Take(Math.Clamp(limit, 1, 100)).ToArrayAsync(ct)).Select(EventView).ToArray();
    }

    // Route known test QR IDs as well as the reserved Ref1. Even a malformed
    // test delivery must never fall through into the shopper payment ledger.
    public async Task<BankAck?> NotificationAsync(byte[] raw, EvidenceMetadata metadata, bool validHeaders, CancellationToken ct)
    {
        if (commerce.Environment != CommerceEnvironment.Uat || commerce.PaymentMode != PaymentMode.Bank) return null;
        var values = Peek(raw);
        var reserved = values.Any(x => x.Key == "Ref1" && x.Value == options.Reference1);
        var qrIds = values.Where(x => x.Key == "QRId").Select(x => x.Value).ToArray();
        if (!reserved && !await db.BankTestCases.AnyAsync(x => qrIds.Contains(x.QrId), ct)) return null;
        Guard(); db.ChangeTracker.Clear();
        CreditEvidence? credit = null;
        try { if (validHeaders) credit = BankWire.ParseNotice(raw); }
        catch (Exception exception) when (exception is JsonException or FormatException) { }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow(); BankTestCase? test = null;
        var code = "0001"; var outcome = validHeaders ? "InvalidInput" : "InvalidHeaders";
        var instruction = credit?.InstructionId ?? Clip(values.FirstOrDefault(x => x.Key == "InstructionId").Value, 50);
        if (credit is not null)
        {
            // Serialize with the shopper ledger too: one bank instruction cannot
            // be accepted concurrently into both namespaces.
            var sharedKey = $"bank:{commerce.Environment}:{ttb.MerchantId}:{credit.InstructionId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({sharedKey}, 0))", ct);
            await LockAsync("receipt:" + credit.InstructionId, ct);
            var prior = await db.BankTestReceipts.SingleOrDefaultAsync(x => x.MerchantId == ttb.MerchantId && x.InstructionId == credit.InstructionId, ct);
            if (prior is not null)
            {
                if (prior.CaseId is { } oldId) test = await LockedCaseAsync(oldId, ct);
                var identical = prior.BusinessHash == BankWire.BusinessHash(credit);
                code = identical ? prior.AckCode == "0000" ? "0004" : prior.AckCode : "0001";
                outcome = identical ? "DuplicateNotification" : "InstructionConflict";
                if (!identical && test is not null) test.State = "Review";
                prior.DeliveryCount++; prior.LastReceivedAt = now;
            }
            else
            {
                await LockAsync("qr:" + credit.QrId, ct);
                var id = await db.BankTestCases.Where(x => x.QrId == credit.QrId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
                if (id is not null) test = await LockedCaseAsync(id.Value, ct);
                if (await CustomerTargetAsync(credit.QrId, credit.InstructionId, ct)) { code = "0006"; outcome = "CustomerTargetRejected"; }
                else if (credit.BillerNo != ttb.BillerNo) { code = "0003"; outcome = "BillerMismatch"; }
                else if (credit.Ref1 != (test?.Ref1 ?? options.Reference1)) { code = "0006"; outcome = "Ref1Mismatch"; }
                else if (credit.AmountMinor > options.MaximumAmountMinor) { code = "0005"; outcome = "TestAmountLimit"; }
                else
                {
                    if (test is null)
                    {
                        test = new BankTestCase { MerchantId = ttb.MerchantId, BillerNo = ttb.BillerNo, Ref1 = credit.Ref1,
                            Ref2 = credit.Ref2 ?? "", QrId = credit.QrId, AmountMinor = credit.AmountMinor,
                            Origin = "BankCallback", State = "Observed", CreatedAt = now };
                        db.BankTestCases.Add(test);
                    }
                    (code, outcome) = Match(test, credit);
                    if (code == "0000")
                    {
                        if (test.InstructionId is not null && test.InstructionId != credit.InstructionId) { test.State = "Review"; outcome = "ExtraTestCredit"; }
                        else
                        {
                            test.InstructionId = credit.InstructionId; test.PaymentObservedAt ??= now;
                            test.CrossBorder = credit.CrossBorderFlag == "Y";
                            if (test.State is not ("Voided" or "VoidUnknown" or "Review")) test.State = test.Origin == "ConsoleQr" ? "Paid" : "Observed";
                        }
                    }
                    else test.State = "Review";
                }
                db.BankTestReceipts.Add(new BankTestReceipt { CaseId = test?.Id, MerchantId = ttb.MerchantId,
                    InstructionId = credit.InstructionId, BusinessHash = BankWire.BusinessHash(credit), AckCode = code,
                    CreatedAt = now, LastReceivedAt = now });
            }
        }
        var ack = Ack(instruction, code);
        var entry = new BankTestEvent { CaseId = test?.Id, Kind = "Notification", Outcome = outcome,
            Ref1 = credit?.Ref1 ?? Clip(values.FirstOrDefault(x => x.Key == "Ref1").Value, 20), Ref2 = credit?.Ref2 ?? "",
            QrId = credit?.QrId ?? Clip(values.FirstOrDefault(x => x.Key == "QRId").Value, 20), InstructionId = instruction,
            AmountMinor = credit?.AmountMinor, RequestUid = metadata.RequestUid, HttpStatus = 200, BankCode = code,
            RawPayload = raw, PayloadHash = BankWire.Hash(raw), PayloadJson = Redact(raw),
            ResponseJson = JsonSerializer.Serialize(ack, BankWire.Json), MetadataJson = JsonSerializer.Serialize(metadata, CatalogService.Json), CreatedAt = now };
        db.BankTestEvents.Add(entry); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); Log(entry);
        return ack;
    }

    private Task<int> LockAsync(string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"bank-test:" + key}, 0))", ct);
    private async Task<BankTestCase> LockedCaseAsync(Guid id, CancellationToken ct) =>
        (await db.BankTestCases.FromSqlInterpolated($"SELECT * FROM ttb.\"BankTestCases\" WHERE \"Id\"={id} FOR UPDATE").ToListAsync(ct)).SingleOrDefault() ?? throw Missing();
    private async Task<bool> CustomerTargetAsync(string qr, string? instruction, CancellationToken ct) =>
        await db.Attempts.AnyAsync(x => x.QrId == qr, ct) ||
        await db.BankTransactions.AnyAsync(x => x.QrId == qr || instruction != null && x.InstructionId == instruction, ct);

    private static (string Code, string Outcome) Match(BankTestCase row, CreditEvidence credit) =>
        row.BillerNo != credit.BillerNo ? ("0003", "BillerMismatch") : row.QrId != credit.QrId ? ("0002", "QrMismatch") :
        row.Ref1 != credit.Ref1 ? ("0006", "Ref1Mismatch") : credit.Ref2 is not null && row.Ref2 != credit.Ref2 ? ("0007", "Ref2Mismatch") :
        row.AmountMinor != credit.AmountMinor ? ("0005", "AmountMismatch") : ("0000", "TestCreditCaptured");

    private BankAck Ack(string instruction, string code) => new(instruction, code, code switch
    { "0000" => "Successful", "0004" => "Duplicate Instruction ID", "0003" => "Invalid BillerNo", "0005" => "Amount mismatch", "0006" => "Ref1 mismatch", "0007" => "Ref2 mismatch", _ => "Invalid Input" }, TtbBankGateway.BankTime(clock.GetUtcNow()));
    private static string Clip(string? value, int length) => value is null || value.Any(char.IsControl) ? "" : value[..Math.Min(value.Length, length)];
    private static KeyValuePair<string, string>[] Peek(byte[] raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 16 });
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.EnumerateObject()
                .Where(x => x.Value.ValueKind == JsonValueKind.String && x.Name is "Ref1" or "QRId" or "InstructionId")
                .Select(x => new KeyValuePair<string, string>(x.Name, x.Value.GetString()!)).ToArray() : [];
        }
        catch (JsonException) { return []; }
    }

    private static readonly HashSet<string> SafeFields = ["InstructionId", "BillerNo", "Ref1", "Ref2", "QRId", "Amount", "ResultCode", "ResultDesc", "ResCode", "ResDesc", "TransDate", "TransactionId", "VoidAmount", "CrossBorderFlag", "SwitchCode", "CurrencyCode", "SwitchFullName"];
    public static string Redact(byte[] raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 16 });
            var safe = new Dictionary<string, object?>(); var other = new List<string>();
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "{\"jsonObject\":false}";
            foreach (var field in document.RootElement.EnumerateObject())
                if (SafeFields.Contains(field.Name) && field.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False)
                    safe[field.Name] = field.Value.Clone();
                else other.Add(Clip(field.Name, 80));
            if (other.Count > 0) safe["omittedFieldNames"] = other.Distinct().Take(100).ToArray();
            return JsonSerializer.Serialize(safe);
        }
        catch (JsonException) { return "{\"jsonValid\":false}"; }
    }

    private BankTestCaseView View(BankTestCase row, int reads, bool image = false)
    {
        string? png = null;
        if (image && row.Payload.Length > 0)
        {
            using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(row.Payload, QRCodeGenerator.ECCLevel.M);
            using var code = new PngByteQRCode(data); png = "data:image/png;base64," + Convert.ToBase64String(code.GetGraphic(8));
        }
        return new(row.Id, row.Ref1, row.Ref2, row.QrId, row.AmountMinor, row.State, row.Origin, row.InstructionId,
            row.CreatedAt, row.ExpiresAt, ttb.BeneficiaryName, png, reads, options.VoidEnabled);
    }
    private static BankTestEventView EventView(BankTestEvent row) => new(row.Id, row.CaseId, row.OperationId, row.Kind, row.Outcome,
        row.Ref1, row.QrId, row.InstructionId, row.AmountMinor, row.RequestUid, row.HttpStatus, row.BankCode, row.PayloadHash, row.CreatedAt, row.PayloadJson, row.ResponseJson);
    private static CommerceException Missing() => new(404, "bank_test_not_found", "The UAT test case was not found.");
    private void Log(BankTestEvent row) => logger.LogInformation("TTB_BANK_TEST {Summary}", JsonSerializer.Serialize(new
    { row.Id, row.CaseId, row.Kind, row.Outcome, row.Ref1, row.QrId, row.AmountMinor, row.RequestUid, row.HttpStatus, row.BankCode }));
}
