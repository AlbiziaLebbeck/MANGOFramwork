using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class VoidService(CommerceDb db, CommerceOptions commerce, TtbOptions ttb, VoidOptions options, IBankGateway gateway, Numbering numbering, TimeProvider clock)
{
    private sealed record Reserved(Guid OperationId, Guid BankRequestId, Guid RequestUid, string RequestJson, BankOperation Service);
    private string Merchant => commerce.PaymentMode == PaymentMode.Mock ? "SIM" : ttb.MerchantId;
    private string Biller => commerce.PaymentMode == PaymentMode.Mock ? "000000000000000" : ttb.BillerNo;

    public async Task<VoidView> RequestAsync(Guid orderId, VoidInput input, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); Guard(newRequest: true);
        var bankId = await (from allocation in db.Allocations.AsNoTracking() join credit in db.BankTransactions.AsNoTracking() on allocation.BankTransactionId equals credit.Id
                            where allocation.OrderId == orderId && allocation.Primary && credit.Environment == commerce.Environment && credit.MerchantId == Merchant && credit.BillerNo == Biller
                            select (Guid?)credit.Id).SingleOrDefaultAsync(cancellationToken) ?? throw new CommerceException(409, "void_payment_missing", "There is no primary bank payment for this order.");
        return await RequestBankCoreAsync(bankId, orderId, input, actor, cancellationToken);
    }

    public Task<VoidView> RequestBankAsync(Guid bankId, VoidInput input, ShopperActor actor, CancellationToken cancellationToken) =>
        RequestBankCoreAsync(bankId, null, input, actor, cancellationToken);

    public async Task<VoidReview> InspectBankAsync(Guid bankId, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor);
        var bank = await db.BankTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bankId && x.Environment == commerce.Environment && x.MerchantId == Merchant && x.BillerNo == Biller, cancellationToken) ?? throw Missing();
        var allocation = await db.Allocations.AsNoTracking().SingleOrDefaultAsync(x => x.BankTransactionId == bankId, cancellationToken);
        var attempt = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(x => x.QrId == bank.QrId && x.MerchantId == Merchant, cancellationToken);
        var orderId = allocation?.OrderId ?? attempt?.OrderId;
        var operations = await db.Voids.AsNoTracking().Where(x => x.BankTransactionId == bankId).OrderByDescending(x => x.Id).Take(20).ToArrayAsync(cancellationToken);
        var eligibility = VoidWindow.Check(bank, attempt, options, clock.GetUtcNow());
        try { Guard(newRequest: true); } catch (CommerceException exception) { eligibility = new(false, exception.Code); }
        if (operations.Any(x => x.State != "Rejected")) eligibility = new(false, "void_already_requested");
        if (await db.ManualRefunds.AnyAsync(x => x.BankTransactionId == bankId && x.State != "Cancelled", cancellationToken)) eligibility = new(false, "manual_refund_blocks_void");
        if (allocation?.Primary == true && orderId is not null && !await db.Orders.AnyAsync(x => x.Id == orderId && x.PaymentState == PaymentState.Paid && x.TotalMinor == bank.AmountMinor, cancellationToken)) eligibility = new(false, "void_payment_ineligible");
        var consumed = allocation?.Primary == true && orderId is not null && await ConsumedAsync(orderId.Value, cancellationToken);
        var documents = await db.Documents.AsNoTracking().Where(x => x.BankTransactionId == bankId).OrderBy(x => x.Id).ToArrayAsync(cancellationToken);
        return new(bankId, orderId, bank.AmountMinor.ToString(System.Globalization.CultureInfo.InvariantCulture), allocation?.Primary == true, consumed,
            options.FulfilledRefundPolicyApproved, eligibility, operations.Select(View).ToArray(), documents.Select(ReceiptService.View).ToArray());
    }

    private async Task<VoidView> RequestBankCoreAsync(Guid bankId, Guid? requiredPrimaryOrderId, VoidInput input, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); Guard(newRequest: true);
        if (!Guid.TryParseExact(input.OperationId, "D", out var operationId) || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500 || input.Reason.Any(char.IsControl))
            throw new CommerceException(400, "void_input_invalid", "Provide a stable UUID operation ID and a reason.");
        var key = operationId.ToString("D"); var hash = SecretTokens.Hash(JsonSerializer.Serialize(new { bankId, requiredPrimaryOrderId, reason = input.Reason.Trim(), input.DeliveryReviewed, actor = actor.Key }));
        db.ChangeTracker.Clear();
        var bankLookup = await db.BankTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bankId && x.Environment == commerce.Environment && x.MerchantId == Merchant && x.BillerNo == Biller, cancellationToken) ?? throw Missing();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var operationLock = "void-operation-key:" + key;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({operationLock}, 0))", cancellationToken);
        await InstructionLockAsync(bankLookup.InstructionId, cancellationToken);
        var existing = await db.Voids.SingleOrDefaultAsync(x => x.OperationKey == key, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != hash || existing.BankTransactionId != bankId) throw new CommerceException(409, "operation_conflict", "This operation ID was already used for a different request.");
            return View(existing);
        }
        var bank = await db.BankTransactions.SingleAsync(x => x.Id == bankLookup.Id, cancellationToken);
        var allocation = await db.Allocations.AsNoTracking().SingleOrDefaultAsync(x => x.BankTransactionId == bank.Id, cancellationToken);
        var attempt = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(x => x.QrId == bank.QrId && x.MerchantId == Merchant, cancellationToken);
        var orderId = allocation?.OrderId ?? attempt?.OrderId;
        var order = await OrderLockedAsync(orderId, cancellationToken);
        var affectsOrder = allocation?.Primary == true;
        if (requiredPrimaryOrderId is not null && (!affectsOrder || orderId != requiredPrimaryOrderId)) throw new CommerceException(409, "void_payment_missing", "The primary allocation changed.");
        if (await db.Voids.AnyAsync(x => x.BankTransactionId == bank.Id && x.State != "Rejected", cancellationToken)) throw new CommerceException(409, "void_already_requested", "This payment already has a void operation. Inspect its result before taking another action.");
        if (await db.ManualRefunds.AnyAsync(x => x.BankTransactionId == bank.Id && x.State != "Cancelled", cancellationToken)) throw new CommerceException(409, "manual_refund_blocks_void", "An external-refund case already reserves or attests a return of this credit.");
        if (bank.AmountMinor <= 0 || affectsOrder && (order is null || order.PaymentState != PaymentState.Paid || order.TotalMinor != bank.AmountMinor)) throw new CommerceException(409, "void_payment_ineligible", "Only a complete eligible bank credit can be voided.");
        var eligibility = VoidWindow.Check(bank, attempt, options, clock.GetUtcNow());
        if (!eligibility.Eligible) throw new CommerceException(409, eligibility.Code, "This payment is outside the approved void eligibility policy.");
        if (affectsOrder) await AssertDeliveryPolicyAsync(order!.Id, input.DeliveryReviewed, cancellationToken);
        gateway.EnsureAvailable(BankOperation.MerchantVoid);
        var stamp = TtbBankGateway.BankTime(clock.GetUtcNow());
        await numbering.NextAsync($"void-id:{commerce.Environment}:{Merchant}:{stamp}", 1, cancellationToken);
        var row = new VoidOperation { Environment = commerce.Environment, MerchantId = Merchant, OrderId = orderId, AffectsOrderPayment = affectsOrder, BankTransactionId = bank.Id,
            OperationKey = key, RequestHash = hash, TransactionId = Merchant + stamp, InstructionId = bank.InstructionId, QrId = bank.QrId, BillerNo = bank.BillerNo,
            AmountMinor = bank.AmountMinor, Actor = actor.Key, OperatorName = actor.DisplayName, Reason = input.Reason.Trim(), DeliveryReviewed = input.DeliveryReviewed,
            PolicyJson = JsonSerializer.Serialize(new { options.WindowPolicy, options.CutoffSafetySeconds, eligibility, options.FulfilledRefundPolicyApproved }, CatalogService.Json), CreatedAt = clock.GetUtcNow() };
        db.Voids.Add(row);
        if (order is not null) { if (affectsOrder) order.PaymentState = PaymentState.VoidPending; order.State = OrderState.Review; }
        db.Jobs.Add(DurableWork.Enqueue($"void-send:{row.Id}", "void.send", JsonSerializer.Serialize(new { voidId = row.Id })));
        db.AuditEvents.Add(new AuditEvent { Actor = actor.Key, Action = "void.request", Resource = $"void:{row.Id}", DataJson = JsonSerializer.Serialize(new { orderId, bankId = bank.Id, row.TransactionId, row.AmountMinor, row.PolicyJson }) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return View(row);
    }

    public async Task SendAsync(Guid id, CancellationToken cancellationToken)
    {
        var reserved = await ReserveSendAsync(id, cancellationToken);
        if (reserved is not null) await ExecuteAsync(reserved, cancellationToken);
    }

    private async Task<Reserved?> ReserveSendAsync(Guid id, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear(); var lookup = await db.Voids.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
        Guard(newRequest: false); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await InstructionLockAsync(lookup.InstructionId, cancellationToken); var order = await OrderLockedAsync(lookup.OrderId, cancellationToken);
        var row = await OperationLockedAsync(id, cancellationToken);
        // The durable fence is the authority. A retry after a crash can only inspect/enquire, never send this mutation again.
        if (row.SendFencedAt is not null || row.State != "Requested") return null;
        var bank = await db.BankTransactions.SingleAsync(x => x.Id == row.BankTransactionId, cancellationToken);
        if (bank.Voided)
        {
            row.State = "Confirmed"; row.ConfirmedAt = clock.GetUtcNow(); row.ConfirmationSource = "ExistingTrustedBankEvidence";
            await VoidLedger.ConfirmAsync(db, bank, order, row.ConfirmationSource, $"void:{id}", clock, cancellationToken);
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return null;
        }
        var attempt = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(x => x.QrId == row.QrId && x.MerchantId == Merchant, cancellationToken);
        var eligibility = VoidWindow.Check(bank, attempt, options, clock.GetUtcNow());
        if (!options.Enabled || !eligibility.Eligible)
        {
            row.State = "Rejected"; row.ResultCode = !options.Enabled ? "LOCAL_DISABLED_BEFORE_SEND" : "LOCAL_WINDOW_CLOSED";
            if (row.AffectsOrderPayment && order?.PaymentState == PaymentState.VoidPending) order.PaymentState = PaymentState.Paid;
            await ReviewCaseAsync(row, cancellationToken); await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return null;
        }
        gateway.EnsureAvailable(BankOperation.MerchantVoid);
        var request = BankRequest(row, BankOperation.MerchantVoid); db.VoidRequests.Add(request);
        row.SendFencedAt = clock.GetUtcNow(); row.State = "Unknown";
        db.AuditEvents.Add(new AuditEvent { Actor = "void-worker", Action = "void.send-fence", Resource = $"void:{id}", CorrelationId = request.RequestUid,
            DataJson = JsonSerializer.Serialize(new { options.WindowPolicy, options.CutoffSafetySeconds, eligibility, options.FulfilledRefundPolicyApproved }, CatalogService.Json) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(id, request.Id, Guid.Parse(request.RequestUid), request.RequestJson, BankOperation.MerchantVoid);
    }

    public async Task<VoidView> EnquireAsync(Guid id, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); Guard(newRequest: false); db.ChangeTracker.Clear();
        var lookup = await db.Voids.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Environment == commerce.Environment && x.MerchantId == Merchant, cancellationToken) ?? throw Missing();
        Reserved reserved;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await InstructionLockAsync(lookup.InstructionId, cancellationToken); await OrderLockedAsync(lookup.OrderId, cancellationToken);
            var row = await OperationLockedAsync(id, cancellationToken); var now = clock.GetUtcNow();
            if (row.State is "Confirmed" or "Rejected") return View(row);
            if (row.SendFencedAt is null) throw new CommerceException(409, "void_not_sent", "The void has not crossed the send fence; no bank enquiry is needed.");
            if (row.EnquiryLeaseUntil > now) throw new CommerceException(409, "void_enquiry_in_progress", "A void enquiry is already in progress.");
            if (row.EnquiryCount >= options.EnquiryBudget) throw new CommerceException(429, "void_enquiry_budget_exhausted", "The bounded void enquiry budget is exhausted; reconcile with bank reports/support.");
            if (row.LastEnquiryAt?.AddSeconds(options.EnquiryCooldownSeconds) > now) throw new CommerceException(429, "void_enquiry_cooldown", "Wait before another void enquiry.");
            gateway.EnsureAvailable(BankOperation.EnquiryVoid); await BankReadBudget.ReserveAsync(db, Merchant, commerce.BankReadsPerMinute, now, cancellationToken);
            var request = BankRequest(row, BankOperation.EnquiryVoid); db.VoidRequests.Add(request); row.EnquiryCount++; row.LastEnquiryAt = now;
            row.EnquiryLeaseToken = request.Id; row.EnquiryLeaseUntil = now.AddSeconds(60);
            db.AuditEvents.Add(new AuditEvent { Actor = actor.Key, Action = "void.enquire", Resource = $"void:{id}", CorrelationId = request.RequestUid });
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            reserved = new(id, request.Id, Guid.Parse(request.RequestUid), request.RequestJson, BankOperation.EnquiryVoid);
        }
        await ExecuteAsync(reserved, cancellationToken); return await GetAsync(id, actor, cancellationToken);
    }

    private async Task ExecuteAsync(Reserved reserved, CancellationToken cancellationToken)
    {
        BankReply reply;
        try
        {
            using var document = JsonDocument.Parse(reserved.RequestJson);
            reply = await gateway.PostAsync(reserved.Service, document.RootElement, reserved.RequestUid, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await RecordAsync(reserved, new(null, [], "CancelledAfterFence"), cleanup.Token); } catch (Exception exception) when (exception is not OutOfMemoryException) { }
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { reply = new(null, [], "TransportOrSigningFailure"); }
        await RecordAsync(reserved, reply, cancellationToken);
    }

    private async Task RecordAsync(Reserved reserved, BankReply reply, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear(); var lookup = await db.Voids.AsNoTracking().SingleAsync(x => x.Id == reserved.OperationId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await InstructionLockAsync(lookup.InstructionId, cancellationToken); var order = await OrderLockedAsync(lookup.OrderId, cancellationToken);
        var row = await OperationLockedAsync(reserved.OperationId, cancellationToken); var bank = await db.BankTransactions.SingleAsync(x => x.Id == row.BankTransactionId, cancellationToken);
        var request = await db.VoidRequests.SingleAsync(x => x.Id == reserved.BankRequestId, cancellationToken);
        if (request.CompletedAt is not null) return;
        var result = VoidProtocol.Parse(reserved.Service, reply, row);
        request.RawResponse = reply.Body; request.ResponseSha256 = BankWire.Hash(reply.Body); request.HttpStatus = reply.StatusCode;
        request.Outcome = result.Outcome; request.ResultCode = result.ResultCode; request.CompletedAt = clock.GetUtcNow();
        row.ResultCode = result.ResultCode;
        if (result.Outcome is "Confirmed" or "AlreadyVoided" || bank.Voided)
        {
            row.State = "Confirmed"; row.ConfirmedAt ??= clock.GetUtcNow(); row.ConfirmationSource ??= bank.Voided ? "ExistingTrustedBankEvidence" : reserved.Service + ":" + result.Outcome;
            await VoidLedger.ConfirmAsync(db, bank, order, row.ConfirmationSource, $"void-request:{request.Id}", clock, cancellationToken);
            var holdKey = $"void-delivery-review:{bank.Id}";
            if (row.AffectsOrderPayment && order is not null && !await db.Cases.AnyAsync(x => x.EffectKey == holdKey, cancellationToken)) db.Cases.Add(new ReconciliationCase { EffectKey = holdKey, Kind = "VoidFulfillmentReview", OrderId = order.Id, BankTransactionId = bank.Id, EvidenceJson = JsonSerializer.Serialize(new { voidId = row.Id }) });
        }
        else if (result.Outcome == "Rejected" && reserved.Service == BankOperation.MerchantVoid)
        { row.State = "Rejected"; if (row.AffectsOrderPayment && order?.PaymentState == PaymentState.VoidPending) order.PaymentState = PaymentState.Paid; }
        else { row.State = "Unknown"; if (row.AffectsOrderPayment && order is not null) order.PaymentState = PaymentState.VoidPending; }
        // A rejected/uncertain void keeps the delivery hold for an explicit finance resolution.
        if (order is not null) order.State = OrderState.Review;
        if (row.EnquiryLeaseToken == request.Id) { row.EnquiryLeaseToken = null; row.EnquiryLeaseUntil = null; }
        await ReviewCaseAsync(row, cancellationToken);
        db.AuditEvents.Add(new AuditEvent { Actor = "bank-void", Action = "void.result", Resource = $"void:{row.Id}", CorrelationId = request.RequestUid,
            DataJson = JsonSerializer.Serialize(new { requestId = request.Id, result.Outcome, result.ResultCode, row.State, row.ConfirmationSource }) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task<VoidView> GetAsync(Guid id, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); var row = await db.Voids.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Environment == commerce.Environment && x.MerchantId == Merchant, cancellationToken) ?? throw Missing(); return View(row);
    }
    private async Task ReviewCaseAsync(VoidOperation row, CancellationToken cancellationToken)
    {
        var key = $"void-review:{row.Id}"; var review = await db.Cases.SingleOrDefaultAsync(x => x.EffectKey == key, cancellationToken);
        if (review is null && row.State != "Confirmed")
        { review = new ReconciliationCase { EffectKey = key, Kind = "VoidOperationReview", OrderId = row.OrderId, BankTransactionId = row.BankTransactionId, EvidenceJson = JsonSerializer.Serialize(new { voidId = row.Id }) }; db.Cases.Add(review); }
        if (review is not null && row.State == "Confirmed" && review.State == "Open")
        { review.State = "Resolved"; review.Version++; review.ResolvedAt = clock.GetUtcNow(); review.ResolutionJson = JsonSerializer.Serialize(new { reason = "BankVoidConfirmed", voidId = row.Id, row.ConfirmationSource }); }
    }
    private async Task AssertDeliveryPolicyAsync(Guid orderId, bool acknowledged, CancellationToken cancellationToken)
    {
        var consumed = await ConsumedAsync(orderId, cancellationToken);
        if (consumed && (!options.FulfilledRefundPolicyApproved || !acknowledged)) throw new CommerceException(409, "void_delivery_review_required", "Delivered/downloaded/used items require the approved fulfilled-refund policy and an explicit finance acknowledgment.");
    }
    private async Task<bool> ConsumedAsync(Guid orderId, CancellationToken cancellationToken) =>
        await db.Fulfillments.AnyAsync(x => x.OrderId == orderId && (x.State == FulfillmentState.Shipped || x.State == FulfillmentState.Delivered), cancellationToken) ||
            await db.Entitlements.AnyAsync(x => x.OrderId == orderId && x.DownloadRequestedAt != null, cancellationToken) ||
            await db.GameTickets.AnyAsync(x => x.UsedAt != null && db.Entitlements.Any(e => e.Id == x.EntitlementId && e.OrderId == orderId), cancellationToken);
    private VoidBankRequest BankRequest(VoidOperation row, BankOperation operation) => new()
    {
        VoidOperationId = row.Id, Service = operation == BankOperation.MerchantVoid ? "merchant-void" : "enquiry-void", RequestUid = Guid.NewGuid().ToString("D"), CreatedAt = clock.GetUtcNow(),
        RequestJson = JsonSerializer.Serialize(new { row.TransactionId, row.InstructionId, QRId = row.QrId, row.BillerNo, TransDate = TtbBankGateway.BankTime(clock.GetUtcNow()) }, BankWire.Json)
    };
    private async Task<Order?> OrderLockedAsync(Guid? id, CancellationToken cancellationToken) => id is null ? null :
        (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {id.Value} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault() ?? throw Missing();
    private async Task<VoidOperation> OperationLockedAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Voids.FromSqlInterpolated($"SELECT * FROM ttb.\"Voids\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault(x => x.Environment == commerce.Environment && x.MerchantId == Merchant) ?? throw Missing();
    private Task<int> InstructionLockAsync(string instruction, CancellationToken cancellationToken)
    {
        var key = $"bank:{commerce.Environment}:{Merchant}:{instruction}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
    private void Guard(bool newRequest)
    {
        var simulated = commerce.Environment == CommerceEnvironment.Local && commerce.PaymentMode == PaymentMode.Mock && options.LocalSimulationEnabled;
        if ((!simulated && (commerce.PaymentMode != PaymentMode.Bank || commerce.Environment == CommerceEnvironment.Local || !options.ProfileApproved)) || !options.ValidPolicy || newRequest && !options.Enabled)
            throw new CommerceException(503, "void_unavailable", "The approved bank void policy is not available.");
    }
    private static void Finance(ShopperActor actor) { if (!actor.HasRole("finance")) throw new CommerceException(403, "role_required", "Finance permission is required."); }
    private VoidView View(VoidOperation row) => new(row.Id, row.OrderId, row.TransactionId, row.State, row.ResultCode, row.EnquiryCount, Math.Max(0, options.EnquiryBudget - row.EnquiryCount), row.CreatedAt, row.ConfirmedAt, row.ConfirmationSource);
    private static CommerceException Missing() => new(404, "not_found", "The void operation was not found.");
}

public sealed class VoidSendHandler(VoidService service) : ICommerceJobHandler
{
    public string Kind => "void.send";
    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(payloadJson); await service.SendAsync(json.RootElement.GetProperty("voidId").GetGuid(), cancellationToken);
    }
}
