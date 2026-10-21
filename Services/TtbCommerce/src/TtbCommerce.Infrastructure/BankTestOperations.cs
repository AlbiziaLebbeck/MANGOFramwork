using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed partial class BankTestConsoleService
{
    public async Task<BankTestOperationView> ActionAsync(Guid caseId, string kind, BankTestActionInput input, CancellationToken ct)
    {
        Guard();
        var operation = kind switch { "verify" => BankOperation.Verify, "void" => BankOperation.MerchantVoid,
            "enquiry-void" => BankOperation.EnquiryVoid, _ => throw new CommerceException(400, "bank_test_action_invalid", "Choose verify, void or enquiry-void.") };
        if (!Guid.TryParseExact(input.OperationId, "D", out var key))
            throw new CommerceException(400, "operation_id_invalid", "A stable UUID operation ID is required.");
        if (kind == "void" && input.ConfirmReference != options.Reference1)
            throw new CommerceException(400, "bank_test_void_confirmation", "Explicitly confirm the reserved test reference before Void.");

        BankTestOperation request;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await LockAsync("operation:" + key.ToString("D"), ct);
            var prior = await db.BankTestOperations.SingleOrDefaultAsync(x => x.OperationKey == key.ToString("D"), ct);
            if (prior is not null)
            {
                if (prior.CaseId != caseId || prior.Kind != kind) throw new CommerceException(409, "operation_conflict", "This operation ID belongs to another test request.");
                await transaction.CommitAsync(ct); return OperationView(prior);
            }
            var test = await LockedCaseAsync(caseId, ct); var now = clock.GetUtcNow();
            if (test.MerchantId != ttb.MerchantId || test.BillerNo != ttb.BillerNo || test.Ref1 != options.Reference1)
                throw new CommerceException(409, "bank_test_profile_changed", "The test belongs to another bank/reference profile.");
            if (await CustomerTargetAsync(test.QrId, test.InstructionId, ct))
                throw new CommerceException(409, "bank_test_customer_target", "Test actions cannot target a shopper QR or payment.");
            if (test.LastRequestAt?.AddSeconds(commerce.VerificationCooldownSeconds) > now)
                throw new CommerceException(429, "bank_test_cooldown", "Wait before another bank request.");
            if (await db.BankTestOperations.AnyAsync(x => x.CaseId == caseId && x.CompletedAt == null && x.SendFencedAt > now.AddSeconds(-60), ct))
                throw new CommerceException(409, "bank_test_request_in_progress", "A request is in progress. Refresh its status instead of resending.");

            var transactionId = ""; var instruction = test.InstructionId ?? "";
            object body;
            if (operation == BankOperation.Verify)
            {
                if (now < test.CreatedAt.AddSeconds(commerce.VerificationDelaySeconds))
                    throw new CommerceException(409, "verification_wait", "Wait at least60seconds after test creation before Verify.");
                if (await db.BankTestOperations.CountAsync(x => x.CaseId == caseId && x.Kind == kind, ct) >= options.ReadBudget)
                    throw new CommerceException(429, "bank_test_read_budget", "The test Verify budget is exhausted. Create a new test case.");
                body = new { test.BillerNo, QRId = test.QrId, TransDate = TtbBankGateway.BankTime(now) };
            }
            else if (operation == BankOperation.MerchantVoid)
            {
                if (!options.VoidEnabled) throw new CommerceException(503, "bank_test_void_disabled", "Test Void is not enabled.");
                if (test.Origin != "ConsoleQr" || test.PaymentObservedAt is null || test.InstructionId is null || test.State != "Paid")
                    throw new CommerceException(409, "bank_test_void_unproven", "Void requires a paid test QR created by this console, with matching bank evidence.");
                if (await db.BankTestOperations.AnyAsync(x => x.CaseId == caseId && x.Kind == "void", ct))
                    throw new CommerceException(409, "bank_test_void_already_sent", "A Void was already fenced. Use Enquiry Void; do not resend.");
                var eligibility = VoidWindow.Check(new BankTransaction { InstructionId = instruction, AmountMinor = test.AmountMinor,
                        CreatedAt = test.PaymentObservedAt.Value, CrossBorderFlag = test.CrossBorder ? "Y" : null },
                    new PaymentAttempt { CreatedAt = test.CreatedAt },
                    new VoidOptions { WindowPolicy = VoidWindowPolicy.SameCalendarDayBefore2256 }, now);
                if (!eligibility.Eligible) throw new CommerceException(409, eligibility.Code, "The test is outside the conservative same-day Void window.");
                var stamp = TtbBankGateway.BankTime(now);
                // Same merchant/second fence as shopper Void: transaction IDs cannot collide.
                await numbering.NextAsync($"void-id:{commerce.Environment}:{ttb.MerchantId}:{stamp}", 1, ct);
                transactionId = ttb.MerchantId + stamp;
                body = new { TransactionId = transactionId, InstructionId = instruction, QRId = test.QrId, test.BillerNo, TransDate = stamp };
            }
            else
            {
                var original = await db.BankTestOperations.AsNoTracking().SingleOrDefaultAsync(x => x.CaseId == caseId && x.Kind == "void", ct)
                    ?? throw new CommerceException(409, "bank_test_void_missing", "No test Void has been sent for this case.");
                if (await db.BankTestOperations.CountAsync(x => x.CaseId == caseId && x.Kind == kind, ct) >= 3)
                    throw new CommerceException(429, "bank_test_enquiry_budget", "The test Void enquiry budget is exhausted.");
                transactionId = original.TransactionId; instruction = original.InstructionId;
                body = new { TransactionId = transactionId, InstructionId = instruction, QRId = test.QrId, test.BillerNo, TransDate = TtbBankGateway.BankTime(now) };
            }
            gateway.EnsureAvailable(operation);
            if (operation != BankOperation.MerchantVoid) await BankReadBudget.ReserveAsync(db, ttb.MerchantId, commerce.BankReadsPerMinute, now, ct);
            request = new BankTestOperation { CaseId = caseId, OperationKey = key.ToString("D"), Kind = kind,
                RequestUid = Guid.NewGuid().ToString("D"), TransactionId = transactionId, InstructionId = instruction,
                RequestJson = JsonSerializer.Serialize(body, BankWire.Json), SendFencedAt = now, CreatedAt = now };
            db.BankTestOperations.Add(request); test.LastRequestAt = now;
            if (kind == "void") test.State = "VoidUnknown";
            var entry = new BankTestEvent { CaseId = caseId, OperationId = request.Id, Kind = kind, Outcome = "Unknown",
                Ref1 = test.Ref1, Ref2 = test.Ref2, QrId = test.QrId, InstructionId = instruction, AmountMinor = test.AmountMinor,
                RequestUid = request.RequestUid, PayloadJson = request.RequestJson, CreatedAt = now };
            db.BankTestEvents.Add(entry);
            // Commit the send fence before leaving the DB. A crash/cancellation
            // never authorizes an automatic retry of Merchant Void.
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); Log(entry);
        }

        BankReply reply;
        try
        {
            using var document = JsonDocument.Parse(request.RequestJson);
            reply = await gateway.PostAsync(operation, document.RootElement.Clone(), Guid.Parse(request.RequestUid), ct);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await FinishActionAsync(request.Id, operation, new(null, [], exception is OperationCanceledException ? "CancelledAfterFence" : "RequestFailedAfterFence"), cleanup.Token);
            throw;
        }
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await FinishActionAsync(request.Id, operation, reply, completion.Token);
    }

    public async Task<BankTestOperationView[]> OperationsAsync(Guid caseId, CancellationToken ct)
    {
        Guard(); return (await db.BankTestOperations.AsNoTracking().Where(x => x.CaseId == caseId).OrderByDescending(x => x.Id).Take(50).ToArrayAsync(ct))
            .Select(OperationView).ToArray();
    }

    private async Task<BankTestOperationView> FinishActionAsync(Guid id, BankOperation operation, BankReply reply, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await db.BankTestOperations.SingleAsync(x => x.Id == id, ct);
        var test = await LockedCaseAsync(request.CaseId, ct);
        var entry = await db.BankTestEvents.SingleAsync(x => x.OperationId == id, ct);
        var outcome = "Unknown"; string? code = null;
        if (operation == BankOperation.Verify)
        {
            var result = BankReadResult.Parse(operation, reply, test.BillerNo, test.QrId); outcome = result.Outcome; code = result.ResCode;
            if (result.Outcome == "Found")
            {
                var credit = result.Credits.Single(); var match = Match(test, credit);
                if (match.Code != "0000") { outcome = match.Outcome; test.State = "Review"; }
                else if (await CustomerTargetAsync(test.QrId, credit.InstructionId, ct)) { outcome = "CustomerTargetRejected"; test.State = "Review"; }
                else if (test.InstructionId is not null && test.InstructionId != credit.InstructionId) { outcome = "InstructionConflict"; test.State = "Review"; }
                else
                {
                    test.InstructionId = credit.InstructionId; test.PaymentObservedAt ??= clock.GetUtcNow(); test.CrossBorder = credit.CrossBorderFlag == "Y";
                    if (test.State is not ("Voided" or "VoidUnknown" or "Review")) test.State = "Paid";
                }
            }
            else if (result.Outcome == "AlreadyVoided") test.State = "Voided";
        }
        else
        {
            var result = VoidProtocol.Parse(operation, reply, new VoidOperation { TransactionId = request.TransactionId,
                InstructionId = request.InstructionId, QrId = test.QrId, BillerNo = test.BillerNo });
            outcome = result.Outcome; code = result.ResultCode;
            if (outcome is "Confirmed" or "AlreadyVoided") test.State = "Voided";
            else if (operation == BankOperation.MerchantVoid) test.State = outcome == "Rejected" ? "Paid" : "VoidUnknown";
        }
        request.Outcome = outcome; request.ResultCode = code; request.HttpStatus = reply.StatusCode;
        request.TransportError = reply.TransportError; request.CompletedAt = clock.GetUtcNow();
        entry.Outcome = outcome; entry.BankCode = code; entry.HttpStatus = reply.StatusCode;
        entry.RawPayload = reply.Body; entry.PayloadHash = BankWire.Hash(reply.Body); entry.ResponseJson = Redact(reply.Body);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); Log(entry); return OperationView(request);
    }

    private static BankTestOperationView OperationView(BankTestOperation row) => new(row.Id, row.CaseId, row.Kind, row.RequestUid,
        row.TransactionId, row.Outcome, row.ResultCode, row.HttpStatus, row.TransportError, row.SendFencedAt, row.CompletedAt);
}
