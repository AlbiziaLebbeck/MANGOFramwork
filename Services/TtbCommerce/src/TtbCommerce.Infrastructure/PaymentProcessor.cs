using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class PaymentProcessor(CommerceDb db, CommerceOptions commerce, TtbOptions ttb, TimeProvider clock, BankTestOptions? bankTests = null)
{
    public async Task<CreditApplication> NoticeAsync(byte[] raw, EvidenceMetadata metadata, CancellationToken cancellationToken)
    {
        CheckSource(metadata.Source);
        try { return await ApplyAsync(BankWire.ParseNotice(raw), raw, metadata, cancellationToken); }
        catch (Exception exception) when (exception is JsonException or FormatException)
        { return await InvalidAsync(raw, metadata, cancellationToken); }
    }

    public async Task<CreditApplication> InvalidAsync(byte[] raw, EvidenceMetadata metadata, CancellationToken cancellationToken)
    {
        CheckSource(metadata.Source);
        ResetTracking();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var hash = BankWire.Hash(raw);
        var key = $"invalid-notice:{hash}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var observation = Observation(raw, metadata); observation.Outcome = "InvalidInput"; db.Observations.Add(observation);
        await CaseAsync(key, "InvalidNotice", null, null, observation.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var instruction = "";
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("InstructionId", out var value) && value.ValueKind == JsonValueKind.String &&
                value.GetString() is { Length: > 0 and <= 50 } id && !id.Any(char.IsControl)) instruction = id;
        }
        catch (JsonException) { }
        return new(Ack(instruction, "0001"), null, null, "InvalidInput");
    }

    public async Task<CreditApplication> ApplyAsync(CreditEvidence evidence, byte[] raw, EvidenceMetadata metadata, CancellationToken cancellationToken)
    {
        CheckSource(metadata.Source);
        if (commerce.Environment == CommerceEnvironment.Uat && evidence.Ref1 == (bankTests?.Reference1 ?? "TEST"))
            throw new CommerceException(409, "bank_test_isolated", "Reserved UAT test references must use the bank test console, not the shopper ledger.");
        var successful = metadata.Source is EvidenceSource.Verify or EvidenceSource.MockVerify ? evidence.ResultCode == "0000" : evidence.ResultCode == "000";
        if (!successful || string.IsNullOrWhiteSpace(evidence.InstructionId) || evidence.InstructionId.Length > 50 || evidence.AmountMinor <= 0)
            return await InvalidAsync(raw, metadata, cancellationToken);
        ResetTracking();
        var merchant = IsMock(metadata.Source) ? "SIM" : ttb.MerchantId;
        var expectedBiller = IsMock(metadata.Source) ? "000000000000000" : ttb.BillerNo;
        var notification = metadata.Source is EvidenceSource.Callback or EvidenceSource.Mock;
        var businessHash = BankWire.BusinessHash(evidence);
        var payloadHash = BankWire.CanonicalHash(raw);
        var instructionLock = $"bank:{commerce.Environment}:{merchant}:{evidence.InstructionId}";
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({instructionLock}, 0))", cancellationToken);
        if (commerce.Environment == CommerceEnvironment.Uat && !IsMock(metadata.Source) &&
            await db.BankTestCases.AnyAsync(x => x.InstructionId == evidence.InstructionId, cancellationToken))
            throw new CommerceException(409, "bank_test_isolated", "This bank instruction belongs to an isolated UAT test.");
        var observation = Observation(raw, metadata); db.Observations.Add(observation);
        if (notification)
        {
            var receipt = await db.CallbackReceipts.SingleOrDefaultAsync(x => x.Environment == commerce.Environment && x.MerchantId == merchant && x.InstructionId == evidence.InstructionId, cancellationToken);
            if (receipt is not null)
            {
                var original = await db.Observations.AsNoTracking().SingleAsync(x => x.Id == receipt.ObservationId, cancellationToken);
                observation.BankTransactionId = original.BankTransactionId; observation.AttemptId = original.AttemptId;
                var orderId = original.AttemptId is null ? (Guid?)null : await db.Attempts.Where(x => x.Id == original.AttemptId).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(cancellationToken);
                receipt.DeliveryCount++; receipt.LastReceivedAt = clock.GetUtcNow();
                string code;
                if (receipt.BusinessHash != businessHash)
                {
                    code = "0001"; observation.Outcome = "InstructionConflict";
                    await CaseAsync($"instruction-conflict:{receipt.Id}:{businessHash}", "InstructionConflict", orderId, original.BankTransactionId, observation.Id, cancellationToken);
                    if (orderId is not null)
                    {
                        var disputed = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId.Value} FOR UPDATE").ToListAsync(cancellationToken)).Single();
                        disputed.State = OrderState.Review; // preserve prior money/allocation; pause any still-pending delivery
                    }
                }
                else
                {
                    code = receipt.AckCode == "0000" ? "0004" : receipt.AckCode; observation.Outcome = "DuplicateNotification";
                    if (receipt.PayloadHash != payloadHash)
                        await CaseAsync($"notice-metadata:{receipt.Id}:{payloadHash}", "NotificationPayloadChanged", orderId, original.BankTransactionId, observation.Id, cancellationToken);
                }
                await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
                return new(Ack(evidence.InstructionId, code), original.BankTransactionId, orderId, observation.Outcome);
            }
        }

        // Lookup first without locking; all mutation paths lock the order before its attempt or inventory rows.
        var lookup = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(x => x.MerchantId == merchant && x.QrId == evidence.QrId, cancellationToken);
        Order? order = null; PaymentAttempt? attempt = null;
        if (lookup is not null)
        {
            order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {lookup.OrderId} FOR UPDATE").ToListAsync(cancellationToken)).Single();
            attempt = (await db.Attempts.FromSqlInterpolated($"SELECT * FROM ttb.\"Attempts\" WHERE \"Id\" = {lookup.Id} FOR UPDATE").ToListAsync(cancellationToken)).Single();
            observation.AttemptId = attempt.Id;
        }
        var bank = await db.BankTransactions.SingleOrDefaultAsync(x => x.Environment == commerce.Environment && x.MerchantId == merchant && x.BillerNo == evidence.BillerNo && x.InstructionId == evidence.InstructionId, cancellationToken);
        var conflict = bank is not null && !Matches(bank, evidence);
        if (bank is null)
        {
            bank = new BankTransaction { Environment = commerce.Environment, MerchantId = merchant, InstructionId = evidence.InstructionId, BillerNo = evidence.BillerNo,
                QrId = evidence.QrId, Ref1 = evidence.Ref1, Ref2 = evidence.Ref2 ?? "", AmountMinor = evidence.AmountMinor, ResultCode = evidence.ResultCode,
                BankTransDate = evidence.TransDate, PaymentTimeSource = "SystemTimeOnly", CrossBorderFlag = evidence.CrossBorderFlag, SwitchCode = evidence.SwitchCode,
                CurrencyCode = evidence.CurrencyCode, SwitchFullName = evidence.SwitchFullName,
                PayerJson = JsonSerializer.Serialize(new { evidence.PayerAccount, evidence.PayerName, evidence.PayerBank }, CatalogService.Json) };
            db.BankTransactions.Add(bank);
        }
        observation.BankTransactionId = bank.Id;
        string ackCode = "0000"; var outcome = "Confirmed";
        if (conflict)
        {
            ackCode = "0001"; outcome = "InstructionConflict";
            await CaseAsync($"bank-conflict:{bank.Id}:{businessHash}", outcome, order?.Id, bank.Id, observation.Id, cancellationToken);
            if (order is not null) order.State = OrderState.Review;
        }
        else if (evidence.BillerNo != expectedBiller || attempt is not null && attempt.BillerNo != evidence.BillerNo)
        { ackCode = "0003"; outcome = "BillerMismatch"; }
        else if (attempt is null || order is null)
        { ackCode = "0002"; outcome = "UnknownQr"; }
        else if (IsMock(metadata.Source) ? attempt.Profile != "MOCK-v1" : attempt.Profile != "TTB-BPQR-ZE-v1")
        { ackCode = "0002"; outcome = "AttemptProfileMismatch"; }
        else if (attempt.ExpectedMinor != evidence.AmountMinor)
        { ackCode = "0005"; outcome = "AmountMismatch"; }
        else if (attempt.Ref1 != evidence.Ref1)
        { ackCode = "0006"; outcome = "Ref1Mismatch"; }
        else if (evidence.Ref2 is not null && attempt.Ref2 != evidence.Ref2)
        { ackCode = "0007"; outcome = "Ref2Mismatch"; }
        else
        {
            if (bank.Ref2.Length == 0 && evidence.Ref2 is not null) bank.Ref2 = evidence.Ref2; // optional Verify field enriched by later callback
            var allocated = await db.Allocations.SingleOrDefaultAsync(x => x.BankTransactionId == bank.Id, cancellationToken);
            if (bank.Voided) outcome = "AlreadyVoided";
            else if (bank.AmountMinor > 0 && await db.ManualRefunds.Where(x => x.BankTransactionId == bank.Id && x.State == "Attested").SumAsync(x => (decimal)x.AmountMinor, cancellationToken) >= bank.AmountMinor)
                outcome = "AlreadyExternallyRefunded";
            else if (allocated is not null)
            {
                if (allocated.OrderId != order.Id) { ackCode = "0001"; outcome = "AllocationConflict"; }
                else outcome = "AlreadyConfirmed"; // first callback after Verify is still ACK0000
            }
            else if (evidence.CrossBorderFlag == "Y" && !ttb.CrossBorderApproved) outcome = "CrossBorderReview";
            else if (evidence.CrossBorderFlag is null && (!string.IsNullOrEmpty(evidence.CurrencyCode) || !string.IsNullOrEmpty(evidence.SwitchCode))) outcome = "CurrencyMetadataReview";
            else if (await db.Allocations.AnyAsync(x => x.OrderId == order.Id && x.Primary, cancellationToken)) outcome = "ExtraCredit";
            else if (order.State != OrderState.Open || order.PaymentState != PaymentState.Unpaid || attempt.State != AttemptState.AwaitingPayment ||
                attempt.ExpiresAt <= clock.GetUtcNow() || order.ExpiresAt <= clock.GetUtcNow()) outcome = "LateOrClosedOrder";
            else if (!await CommitStockAsync(order.Id, cancellationToken)) outcome = "StockReview";
            else
            {
                db.Allocations.Add(new PaymentAllocation { BankTransactionId = bank.Id, OrderId = order.Id, AmountMinor = evidence.AmountMinor });
                order.PaymentState = PaymentState.Paid; order.ConfirmedAt = clock.GetUtcNow(); attempt.State = AttemptState.Confirmed;
                db.Jobs.Add(DurableWork.Enqueue($"receipt:{order.Id}", "receipt.ensure", JsonSerializer.Serialize(new { orderId = order.Id })));
                var lineIds = await db.OrderLines.Where(x => x.OrderId == order.Id).Select(x => x.Id).ToArrayAsync(cancellationToken);
                foreach (var lineId in lineIds) db.Jobs.Add(DurableWork.Enqueue($"fulfill:{lineId}", "fulfillment.dispatch", JsonSerializer.Serialize(new { orderId = order.Id, lineId })));
                db.AuditEvents.Add(new AuditEvent { Actor = $"bank:{metadata.Source}", Action = "payment.confirm", Resource = $"order:{order.Id}", CorrelationId = metadata.RequestUid });
            }
        }
        if (outcome is not "Confirmed" and not "AlreadyConfirmed" and not "AlreadyExternallyRefunded" && !conflict)
        {
            await CaseAsync($"payment:{bank.Id}:{outcome}", outcome, order?.Id, bank.Id, observation.Id, cancellationToken);
            if (order is not null && evidence.BillerNo == expectedBiller)
            {
                order.State = OrderState.Review;
                if (order.PaymentState == PaymentState.Unpaid) order.PaymentState = PaymentState.Review;
                if (attempt is not null && attempt.State != AttemptState.Confirmed) attempt.State = AttemptState.Review;
            }
        }
        observation.Outcome = outcome;
        var ack = Ack(evidence.InstructionId, ackCode);
        if (notification) db.CallbackReceipts.Add(new CallbackReceipt { Environment = commerce.Environment, MerchantId = merchant, InstructionId = evidence.InstructionId,
            PayloadHash = payloadHash, BusinessHash = businessHash, AckCode = ack.ResCode, AckJson = JsonSerializer.Serialize(ack, BankWire.Json), ObservationId = observation.Id });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(ack, bank.Id, order?.Id, outcome);
    }

    private async Task<bool> CommitStockAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var lines = await db.OrderLines.Where(x => x.OrderId == orderId).ToArrayAsync(cancellationToken);
        if (lines.Length == 0) return false;
        OrderLine[] tracked;
        try
        {
            var snapshots = lines.Select(x => (Line: x, Snapshot: JsonSerializer.Deserialize<PurchaseLine>(x.SnapshotJson, CatalogService.Json))).ToArray();
            if (snapshots.Any(x => x.Snapshot is null || x.Snapshot.VariantId != x.Line.VariantId || x.Snapshot.Quantity != x.Line.Quantity ||
                x.Line.Kind == ProductKind.Physical && !x.Snapshot.TrackStock)) return false;
            tracked = snapshots.Where(x => x.Snapshot!.TrackStock).Select(x => x.Line).ToArray();
        }
        catch (JsonException) { return false; }
        if (tracked.Length == 0) return true;
        var lineIds = tracked.Select(x => x.Id).ToArray();
        var reservations = await db.Reservations.Where(x => lineIds.Contains(x.OrderLineId)).ToArrayAsync(cancellationToken);
        if (reservations.Length != tracked.Length || reservations.Any(x => x.State != ReservationState.Reserved || tracked.Single(l => l.Id == x.OrderLineId).Quantity != x.Quantity)) return false;
        var variantIds = reservations.Select(x => x.VariantId).Distinct().ToArray();
        var variants = await db.Variants.FromSqlInterpolated($"SELECT * FROM ttb.\"Variants\" WHERE \"Id\" = ANY({variantIds}) ORDER BY \"Id\" FOR UPDATE").ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var group in reservations.GroupBy(x => x.VariantId))
        {
            var quantity = group.Sum(x => x.Quantity);
            if (!variants.TryGetValue(group.Key, out var variant) || variant.Reserved < quantity || variant.OnHand < quantity) return false;
        }
        foreach (var reservation in reservations)
        {
            var variant = variants[reservation.VariantId]; variant.Reserved -= reservation.Quantity; variant.OnHand -= reservation.Quantity;
            reservation.State = ReservationState.Committed;
            db.StockMovements.Add(new StockMovement { VariantId = variant.Id, OrderLineId = reservation.OrderLineId, EffectKey = $"commit:{reservation.OrderLineId}",
                OnHandDelta = -reservation.Quantity, ReservedDelta = -reservation.Quantity, Reason = "Payment confirmed", Actor = "bank" });
        }
        return true;
    }

    private static bool Matches(BankTransaction bank, CreditEvidence evidence) => bank.QrId == evidence.QrId && bank.Ref1 == evidence.Ref1 &&
        (bank.Ref2.Length == 0 || evidence.Ref2 is null || bank.Ref2 == evidence.Ref2) && bank.AmountMinor == evidence.AmountMinor && bank.CrossBorderFlag == evidence.CrossBorderFlag &&
        bank.SwitchCode == evidence.SwitchCode && bank.CurrencyCode == evidence.CurrencyCode;

    private PaymentObservation Observation(byte[] raw, EvidenceMetadata metadata)
    {
        var hash = BankWire.Hash(raw);
        // Exact bytes are bytea, including malformed UTF8/JSON/NUL. Never force untrusted raw data into PostgreSQL jsonb.
        var json = JsonSerializer.Serialize(new { rawSha256 = hash, byteLength = raw.Length });
        return new() { Source = metadata.Source.ToString(), CorrelationId = metadata.RequestUid, PayloadHash = BankWire.Hash(raw), RawPayload = raw,
            PayloadJson = json, MetadataJson = JsonSerializer.Serialize(metadata, CatalogService.Json), CreatedAt = clock.GetUtcNow() };
    }

    private async Task CaseAsync(string key, string kind, Guid? orderId, Guid? bankId, Guid observationId, CancellationToken cancellationToken)
    {
        if (db.Cases.Local.Any(x => x.EffectKey == key) || await db.Cases.AnyAsync(x => x.EffectKey == key, cancellationToken)) return;
        db.Cases.Add(new ReconciliationCase { EffectKey = key, Kind = kind, OrderId = orderId, BankTransactionId = bankId,
            EvidenceJson = JsonSerializer.Serialize(new { observationId }), CreatedAt = clock.GetUtcNow() });
    }

    private BankAck Ack(string instruction, string code) => new(instruction, code, code switch
    {
        "0000" => "Successful", "0001" => "Invalid Input", "0002" => "Invalid QrID", "0003" => "Invalid BillerNo", "0004" => "Duplicate Instruction ID",
        "0005" => "Amount mismatch", "0006" => "Ref1 mismatch", "0007" => "Ref2 mismatch", _ => "Exception error"
    }, TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Numbering.Bangkok).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));

    private void CheckSource(EvidenceSource source)
    {
        if (!Enum.IsDefined(source) || (IsMock(source) ? commerce.Environment != CommerceEnvironment.Local || commerce.PaymentMode != PaymentMode.Mock : commerce.PaymentMode != PaymentMode.Bank || commerce.Environment == CommerceEnvironment.Local))
            throw new CommerceException(404, "not_found", "The payment operation is unavailable in this environment.");
    }

    private static bool IsMock(EvidenceSource source) => source is EvidenceSource.Mock or EvidenceSource.MockVerify or EvidenceSource.MockEnquiry;

    private void ResetTracking()
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("The evidence processor must own its transaction.");
        // A bank query can take seconds after its budget transaction. Fresh row locks must see any callback/void committed in between.
        db.ChangeTracker.Clear();
    }
}
