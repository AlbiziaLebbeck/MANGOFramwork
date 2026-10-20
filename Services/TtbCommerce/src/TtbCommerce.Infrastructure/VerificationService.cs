using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed record VerificationInput(string Method = "verify");
public sealed record VerificationView(Guid OrderId, string Outcome, int ChecksUsed, int ChecksRemaining, DateTimeOffset? NextCheckAt, string? RequestUid = null);

public sealed class VerificationService(CommerceDb db, CommerceOptions commerce, TtbOptions ttb, IBankGateway gateway, PaymentProcessor payments, TimeProvider clock)
{
    private sealed record ReservedCheck(Guid RequestId, Guid AttemptId, Guid OrderId, string Biller, string QrId, Guid Uid, DateTimeOffset IssuedAt, BankOperation Operation);

    public async Task<VerificationView> CheckAsync(Guid shopperId, Guid orderId, VerificationInput input, CancellationToken cancellationToken)
    {
        var operation = input.Method switch { "verify" => BankOperation.Verify, "enquiry" => BankOperation.EnquiryRange, _ => throw new CommerceException(400, "verification_method_invalid", "Choose verify or enquiry.") };
        if (commerce.PaymentMode == PaymentMode.Mock) MockBankService.Guard(commerce);
        ReservedCheck reserved;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId} AND \"ShopperId\" = {shopperId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault()
                ?? throw new CommerceException(404, "not_found", "The order was not found.");
            var attempt = (await db.Attempts.FromSqlInterpolated($"SELECT * FROM ttb.\"Attempts\" WHERE \"OrderId\" = {orderId} ORDER BY \"CreatedAt\" DESC LIMIT 1 FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault()
                ?? throw new CommerceException(409, "qr_not_issued", "No QR has been issued for this order.");
            if (order.PaymentState is PaymentState.Paid or PaymentState.Voided or PaymentState.Refunded)
                return View(orderId, order.State == OrderState.Review ? "Review" : order.PaymentState.ToString(), attempt);
            var expectedMerchant = commerce.PaymentMode == PaymentMode.Mock ? "SIM" : ttb.MerchantId;
            var expectedProfile = commerce.PaymentMode == PaymentMode.Mock ? "MOCK-v1" : "TTB-BPQR-ZE-v1";
            if (attempt.MerchantId != expectedMerchant || attempt.Profile != expectedProfile) throw new CommerceException(409, "attempt_profile_changed", "This QR needs a finance review before a bank query.");
            var now = clock.GetUtcNow();
            if (now < attempt.CreatedAt.AddSeconds(commerce.VerificationDelaySeconds)) throw new CommerceException(409, "verification_wait", "Wait at least60seconds after QR issuance before checking the bank.");
            if (attempt.VerificationLeaseUntil > now) throw new CommerceException(409, "verification_in_progress", "A bank check is already in progress.");
            if (attempt.VerificationCount >= commerce.VerificationBudget) throw new CommerceException(429, "verification_budget_exhausted", "The bank check limit was reached. Please contact support.");
            if (attempt.LastVerificationAt?.AddSeconds(commerce.VerificationCooldownSeconds) > now) throw new CommerceException(429, "verification_cooldown", "Please wait before checking again.");
            gateway.EnsureAvailable(operation);
            await ReserveMerchantRateAsync(now, cancellationToken);
            var request = new VerificationRequest { AttemptId = attempt.Id, Actor = $"shopper:{shopperId}", Service = operation == BankOperation.Verify ? "verify-qr" : "enquiry-qr-range",
                RequestUid = Guid.NewGuid().ToString("D"), CreatedAt = now };
            db.VerificationRequests.Add(request); attempt.VerificationCount++; attempt.LastVerificationAt = now;
            attempt.VerificationLeaseToken = request.Id; attempt.VerificationLeaseUntil = now.AddSeconds(60);
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            reserved = new(request.Id, attempt.Id, orderId, attempt.BillerNo, attempt.QrId, Guid.Parse(request.RequestUid), attempt.CreatedAt, operation);
        }
        db.ChangeTracker.Clear();
        BankReadResult result;
        try
        {
            var now = clock.GetUtcNow();
            object body;
            if (operation == BankOperation.Verify) body = new { BillerNo = reserved.Biller, QRId = reserved.QrId, TransDate = TtbBankGateway.BankTime(now) };
            else
            {
                var start = reserved.IssuedAt.AddSeconds(-60) > now.AddHours(-24) ? reserved.IssuedAt.AddSeconds(-60) : now.AddHours(-24);
                TtbBankGateway.ValidateRange(start, now, now);
                body = new { BillerNo = reserved.Biller, QRId = reserved.QrId, StartFindDateTime = TtbBankGateway.BankTime(start), EndFindDateTime = TtbBankGateway.BankTime(now), TransDate = TtbBankGateway.BankTime(now) };
            }
            var reply = await gateway.PostAsync(operation, body, reserved.Uid, cancellationToken);
            result = BankReadResult.Parse(operation, reply, reserved.Biller, reserved.QrId);
            var source = commerce.PaymentMode == PaymentMode.Mock ? (operation == BankOperation.Verify ? EvidenceSource.MockVerify : EvidenceSource.MockEnquiry)
                : operation == BankOperation.Verify ? EvidenceSource.Verify : EvidenceSource.Enquiry;
            var metadata = new EvidenceMetadata(source, reserved.Uid.ToString("D"), ServiceName: operation == BankOperation.Verify ? "verify-qr" : "enquiry-qr-range");
            if (result.Outcome == "Found")
            {
                foreach (var credit in result.Credits) await payments.ApplyAsync(credit, reply.Body, metadata, cancellationToken);
            }
            else await RecordReadAsync(reserved, reply, result, metadata, cancellationToken);
            await FinishAsync(reserved, result.Outcome, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Keep the consumed request budget. Cancellation is not proof the bank did not receive the request.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await FinishAsync(reserved, "Unknown", cleanup.Token); } catch (Exception exception) when (exception is not OutOfMemoryException) { }
            throw;
        }
        var fresh = await db.Attempts.AsNoTracking().SingleAsync(x => x.Id == reserved.AttemptId, cancellationToken);
        var actual = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId, cancellationToken);
        var outcome = actual.State == OrderState.Review ? "Review" : actual.PaymentState == PaymentState.Paid ? "Paid" : result.Outcome;
        return View(orderId, outcome, fresh, reserved.Uid.ToString("D"));
    }

    private async Task ReserveMerchantRateAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await BankReadBudget.ReserveAsync(db, commerce.PaymentMode == PaymentMode.Mock ? "SIM" : ttb.MerchantId, commerce.BankReadsPerMinute, now, cancellationToken);
    }

    private async Task FinishAsync(ReservedCheck request, string outcome, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.VerificationRequests.Where(x => x.Id == request.RequestId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Outcome, outcome).SetProperty(x => x.CompletedAt, now), cancellationToken);
        await db.Attempts.Where(x => x.Id == request.AttemptId && x.VerificationLeaseToken == request.RequestId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.VerificationLeaseToken, (Guid?)null).SetProperty(x => x.VerificationLeaseUntil, (DateTimeOffset?)null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RecordReadAsync(ReservedCheck request, BankReply reply, BankReadResult result, EvidenceMetadata metadata, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var observation = new PaymentObservation { AttemptId = request.AttemptId, Source = metadata.Source.ToString(), CorrelationId = metadata.RequestUid,
            PayloadHash = BankWire.Hash(reply.Body), RawPayload = reply.Body, MetadataJson = JsonSerializer.Serialize(metadata, CatalogService.Json),
            PayloadJson = JsonSerializer.Serialize(new { reply.StatusCode, reply.TransportError, result.ResCode, result.Detail }), Outcome = result.Outcome, CreatedAt = clock.GetUtcNow() };
        db.Observations.Add(observation);
        if (result.Outcome != "NotFound")
        {
            var order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {request.OrderId} FOR UPDATE").ToListAsync(cancellationToken)).Single();
            db.Cases.Add(new ReconciliationCase { EffectKey = $"bank-read:{request.RequestId}", Kind = result.Outcome == "AlreadyVoided" ? "BankReportsVoided" : "BankReadUnconfirmed",
                OrderId = request.OrderId, EvidenceJson = JsonSerializer.Serialize(new { observationId = observation.Id, result.Detail }) });
            if (result.Outcome == "AlreadyVoided") { order.State = OrderState.Review; if (order.PaymentState == PaymentState.Unpaid) order.PaymentState = PaymentState.Review; }
        }
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    private VerificationView View(Guid orderId, string outcome, PaymentAttempt attempt, string? uid = null)
    {
        var afterIssue = attempt.CreatedAt.AddSeconds(commerce.VerificationDelaySeconds);
        var afterLast = attempt.LastVerificationAt?.AddSeconds(commerce.VerificationCooldownSeconds) ?? afterIssue;
        var next = afterLast > afterIssue ? afterLast : afterIssue;
        if (attempt.VerificationLeaseUntil > next) next = attempt.VerificationLeaseUntil.Value;
        var remaining = Math.Max(0, commerce.VerificationBudget - attempt.VerificationCount);
        return new(orderId, outcome, attempt.VerificationCount, remaining, remaining == 0 ? null : next, uid);
    }
}
