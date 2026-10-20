using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class FinanceService(CommerceDb db, CommerceOptions commerce, TtbOptions bankOptions, TimeProvider clock)
{
    private string Merchant => commerce.PaymentMode == PaymentMode.Mock ? "SIM" : bankOptions.MerchantId;
    private string Biller => commerce.PaymentMode == PaymentMode.Mock ? "000000000000000" : bankOptions.BillerNo;

    public async Task<FinanceCasePage> CasesAsync(ShopperActor actor, string? state, string? kind, int limit, Guid? before, CancellationToken cancellationToken, Guid? orderId = null)
    {
        Finance(actor); PageLimit(limit);
        if (state is not (null or "Open" or "Resolved") || kind?.Length > 100) throw new CommerceException(400, "case_filter_invalid", "Choose a valid case filter.");
        var query = db.Cases.AsNoTracking().AsQueryable();
        if (orderId is not null) query = query.Where(x => x.OrderId == orderId);
        if (state is not null) query = query.Where(x => x.State == state);
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(x => x.Kind == kind);
        if (before is not null) query = query.Where(x => x.Id.CompareTo(before.Value) < 0);
        var rows = await query.OrderByDescending(x => x.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        var orderIds = rows.Where(x => x.OrderId != null).Select(x => x.OrderId!.Value).Distinct().ToArray();
        var bankIds = rows.Where(x => x.BankTransactionId != null).Select(x => x.BankTransactionId!.Value).Distinct().ToArray();
        var orders = await db.Orders.AsNoTracking().Where(x => orderIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, cancellationToken);
        var banks = await db.BankTransactions.AsNoTracking().Where(x => bankIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var caseIds = rows.Select(x => x.Id).ToArray();
        var manualAmounts = await db.ManualRefunds.AsNoTracking().Where(x => caseIds.Contains(x.CaseId)).ToDictionaryAsync(x => x.CaseId, x => x.AmountMinor, cancellationToken);
        var page = new List<CaseSummary>();
        foreach (var row in rows.Take(limit))
        {
            var bank = row.BankTransactionId is not null ? banks.GetValueOrDefault(row.BankTransactionId.Value) : null;
            page.Add(new(row.Id, row.Version, row.Kind, row.State, row.OrderId, row.OrderId is not null ? orders.GetValueOrDefault(row.OrderId.Value) : null,
                row.BankTransactionId, manualAmounts.TryGetValue(row.Id, out var manualAmount) ? manualAmount.ToString(CultureInfo.InvariantCulture) : bank?.AmountMinor.ToString(CultureInfo.InvariantCulture), bank?.InstructionId, row.Assignee, row.CreatedAt, row.ResolvedAt));
        }
        return new(page.ToArray(), rows.Length > limit ? page[^1].Id : null);
    }

    public async Task<FinanceCaseDetail> CaseAsync(Guid id, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); var row = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw Missing();
        var bank = row.BankTransactionId is null ? null : await BankAsync(row.BankTransactionId.Value, cancellationToken);
        var notes = await db.CaseNotes.AsNoTracking().Where(x => x.CaseId == id).OrderByDescending(x => x.Id).Take(100).Select(x => new CaseNoteView(x.Id, x.Actor, x.Body, x.CreatedAt)).ToArrayAsync(cancellationToken);
        return new(await SummaryAsync(row, cancellationToken), bank, row.EvidenceJson, row.ResolutionJson, notes);
    }

    public async Task<FinanceActionResult> CaseActionAsync(Guid id, CaseActionInput input, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor);
        if (!Guid.TryParseExact(input.OperationId, "D", out var operationKey) || input.ExpectedVersion < 1 || input.Action is not ("note" or "claim" or "dismiss-metadata" or "resolve-voided-credit" or "resolve-manual-refunded-credit" or "accept-late-payment" or "resume-paid-order") ||
            string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000 || input.Reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new CommerceException(400, "case_action_invalid", "Provide an operation ID, current version, supported action and reason.");
        var key = operationKey.ToString("D"); var hash = SecretTokens.Hash(JsonSerializer.Serialize(new { caseId = id, input.ExpectedVersion, input.Action, input.Reason, actor = actor.Key }));
        db.ChangeTracker.Clear(); var lookup = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw Missing();
        var instruction = lookup.BankTransactionId is null ? null : await db.BankTransactions.AsNoTracking().Where(x => x.Id == lookup.BankTransactionId).Select(x => x.InstructionId).SingleAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var operationLock = "finance-operation:" + key;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({operationLock}, 0))", cancellationToken);
        var previous = await db.FinanceOperations.SingleOrDefaultAsync(x => x.OperationKey == key, cancellationToken);
        if (previous is not null)
        {
            if (previous.RequestHash != hash) throw new CommerceException(409, "operation_conflict", "This operation ID already refers to a different finance action.");
            return JsonSerializer.Deserialize<FinanceActionResult>(previous.OutcomeJson, CatalogService.Json)!;
        }
        // All money-related paths take original instruction→order→case. A case-first lock could deadlock a bank result.
        if (instruction is not null)
        {
            var bankLock = $"bank:{commerce.Environment}:{Merchant}:{instruction}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({bankLock}, 0))", cancellationToken);
        }
        if (lookup.OrderId is not null) await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {lookup.OrderId.Value} FOR UPDATE").ToListAsync(cancellationToken);
        var row = (await db.Cases.FromSqlInterpolated($"SELECT * FROM ttb.\"Cases\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        if (row.Version != input.ExpectedVersion || row.OrderId != lookup.OrderId || row.BankTransactionId != lookup.BankTransactionId) throw new CommerceException(409, "case_changed", "Reload the current case before acting.");
        var before = JsonSerializer.Serialize(new { row.Version, row.State, row.Assignee, row.ResolvedAt, row.ResolutionJson });
        if (input.Action == "resume-paid-order")
        {
            if (row.State != "Resolved" || row.OrderId is null) throw new CommerceException(409, "case_financial_resolution_required", "Resolve the financial case before resuming delivery.");
            var order = await db.Orders.SingleAsync(x => x.Id == row.OrderId, cancellationToken);
            if (order.PaymentState != PaymentState.Paid || order.State != OrderState.Review ||
                await db.Cases.AnyAsync(x => x.OrderId == order.Id && x.State == "Open", cancellationToken) ||
                await db.Voids.AnyAsync(x => x.OrderId == order.Id && (x.State == "Requested" || x.State == "Unknown"), cancellationToken) ||
                !await (from a in db.Allocations join b in db.BankTransactions on a.BankTransactionId equals b.Id where a.OrderId == order.Id && a.Primary && a.AmountMinor == order.TotalMinor && !b.Voided && b.Environment == commerce.Environment && b.MerchantId == Merchant && b.BillerNo == Biller select a.Id).AnyAsync(cancellationToken))
                throw new CommerceException(409, "order_resume_ineligible", "Delivery requires an intact primary credit and every financial hold resolved.");
            var lineCount = await db.OrderLines.CountAsync(x => x.OrderId == order.Id, cancellationToken);
            order.State = lineCount > 0 && await db.Fulfillments.CountAsync(x => x.OrderId == order.Id && x.State == FulfillmentState.Delivered, cancellationToken) == lineCount ? OrderState.Completed : OrderState.Open;
        }
        else if (input.Action is "resolve-voided-credit" or "resolve-manual-refunded-credit" or "accept-late-payment")
        {
            if (row.State != "Open" || row.BankTransactionId is null) throw new CommerceException(409, "case_financial_resolution_required", "An open case and trusted bank credit are required.");
            var bank = await db.BankTransactions.SingleOrDefaultAsync(x => x.Id == row.BankTransactionId && x.Environment == commerce.Environment && x.MerchantId == Merchant && x.BillerNo == Biller, cancellationToken)
                ?? throw new CommerceException(409, "case_credit_unavailable", "The credit is outside the configured merchant profile.");
            if (input.Action is "resolve-voided-credit" or "resolve-manual-refunded-credit")
            {
                var returned = input.Action == "resolve-voided-credit" ? bank.Voided :
                    await db.ManualRefunds.Where(x => x.BankTransactionId == bank.Id && x.State == "Attested").SumAsync(x => (decimal)x.AmountMinor, cancellationToken) == bank.AmountMinor;
                if (!returned || row.Kind is not ("ExtraCredit" or "LateOrClosedOrder" or "StockReview" or "UnknownQr" or "AmountMismatch" or "Ref1Mismatch" or "Ref2Mismatch" or "CrossBorderReview" or "CurrencyMetadataReview" or "ReportUnallocatedCredit") ||
                    await db.Allocations.AnyAsync(x => x.BankTransactionId == bank.Id && x.Primary, cancellationToken))
                    throw new CommerceException(409, "case_void_evidence_required", "Only a confirmed refunded non-primary credit can be resolved this way.");
            }
            else await AcceptLatePaymentAsync(row, bank, actor, input.Reason, cancellationToken);
            row.State = "Resolved"; row.ResolvedAt = clock.GetUtcNow(); row.ResolutionJson = JsonSerializer.Serialize(new { input.Action, input.Reason, bankId = bank.Id, bank.InstructionId, bank.Voided, actor = actor.Key });
        }
        else if (input.Action == "dismiss-metadata")
        {
            if (row.State != "Open" || row.Kind is not ("InvalidNotice" or "NotificationPayloadChanged") || row.OrderId is not null && row.Kind != "NotificationPayloadChanged")
                throw new CommerceException(409, "case_financial_resolution_required", "This financial case needs a specific evidence-backed resolution.");
            row.State = "Resolved"; row.ResolvedAt = clock.GetUtcNow(); row.ResolutionJson = JsonSerializer.Serialize(new { input.Action, input.Reason, actor = actor.Key });
        }
        else if (input.Action == "claim")
        {
            if (row.State != "Open") throw new CommerceException(409, "case_closed", "A closed case cannot be assigned.");
            row.Assignee = actor.Key;
        }
        db.CaseNotes.Add(new FinanceCaseNote { CaseId = id, Actor = actor.Key, Body = input.Reason.Trim(), CreatedAt = clock.GetUtcNow() });
        row.Version++;
        var operation = new FinanceOperation { CaseId = id, OrderId = row.OrderId, OperationKey = key, RequestHash = hash, Action = input.Action, Actor = actor.Key, Reason = input.Reason[..Math.Min(500, input.Reason.Length)], BeforeJson = before, CreatedAt = clock.GetUtcNow() };
        var result = new FinanceActionResult(operation.Id, id, row.Version, row.State, input.Action); operation.OutcomeJson = JsonSerializer.Serialize(result, CatalogService.Json); db.FinanceOperations.Add(operation);
        db.AuditEvents.Add(new AuditEvent { Actor = actor.Key, Action = "finance.case." + input.Action, Resource = $"case:{id}", DataJson = JsonSerializer.Serialize(new { operationId = operation.Id, before, after = new { row.Version, row.State, row.Assignee }, reason = input.Reason }) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return result;
    }

    private async Task AcceptLatePaymentAsync(ReconciliationCase review, BankTransaction bank, ShopperActor actor, string reason, CancellationToken cancellationToken)
    {
        if (review.Kind is not ("LateOrClosedOrder" or "StockReview") || review.OrderId is null || bank.Voided || bank.CrossBorderFlag == "Y" ||
            !string.IsNullOrEmpty(bank.CurrencyCode) || !string.IsNullOrEmpty(bank.SwitchCode)) throw new CommerceException(409, "payment_recovery_ineligible", "This case cannot be recovered as an ordinary domestic purchase.");
        var order = await db.Orders.SingleAsync(x => x.Id == review.OrderId, cancellationToken);
        var attempt = await db.Attempts.SingleOrDefaultAsync(x => x.OrderId == order.Id && x.QrId == bank.QrId && x.MerchantId == Merchant && x.BillerNo == Biller, cancellationToken);
        if (attempt is null || bank.Ref1 != attempt.Ref1 || bank.Ref2 != attempt.Ref2 || bank.AmountMinor != order.TotalMinor || bank.AmountMinor != attempt.ExpectedMinor ||
            order.PaymentState is PaymentState.Paid or PaymentState.VoidPending or PaymentState.Voided or PaymentState.Refunded ||
            await db.Allocations.AnyAsync(x => x.BankTransactionId == bank.Id || x.OrderId == order.Id && x.Primary, cancellationToken) ||
            await db.Voids.AnyAsync(x => x.BankTransactionId == bank.Id && x.State != "Rejected", cancellationToken) ||
            await db.ManualRefunds.AnyAsync(x => x.BankTransactionId == bank.Id && x.State != "Cancelled", cancellationToken) ||
            await db.Cases.AnyAsync(x => x.OrderId == order.Id && x.State == "Open" && x.Id != review.Id, cancellationToken))
            throw new CommerceException(409, "payment_recovery_ineligible", "Exact credit identity, one unresolved late-payment case and no allocation/void conflict are required.");
        var lines = await db.OrderLines.Where(x => x.OrderId == order.Id).ToArrayAsync(cancellationToken);
        var parsed = lines.Select(x => (Line: x, Snapshot: JsonSerializer.Deserialize<PurchaseLine>(x.SnapshotJson, CatalogService.Json))).ToArray();
        if (lines.Length == 0 || lines.Sum(x => x.TotalMinor) != order.SubtotalMinor || order.SubtotalMinor + order.ShippingMinor != order.TotalMinor ||
            parsed.Any(x => x.Snapshot is null || x.Snapshot.VariantId != x.Line.VariantId || x.Snapshot.Quantity != x.Line.Quantity || x.Line.Kind == ProductKind.Physical && !x.Snapshot.TrackStock))
            throw new CommerceException(409, "payment_recovery_snapshot_invalid", "The immutable order snapshot needs investigation.");
        var tracked = parsed.Where(x => x.Snapshot!.TrackStock).Select(x => x.Line).ToArray(); var ids = tracked.Select(x => x.Id).ToArray();
        var reservations = await db.Reservations.Where(x => ids.Contains(x.OrderLineId)).ToArrayAsync(cancellationToken);
        if (reservations.Length != tracked.Length || reservations.Any(x => x.State is not (ReservationState.Reserved or ReservationState.Released) || tracked.Single(l => l.Id == x.OrderLineId).Quantity != x.Quantity || tracked.Single(l => l.Id == x.OrderLineId).VariantId != x.VariantId) ||
            await db.StockMovements.AnyAsync(x => ids.Contains(x.OrderLineId!.Value) && x.EffectKey.StartsWith("commit:"), cancellationToken))
            throw new CommerceException(409, "payment_recovery_stock_inconsistent", "The existing stock ledger requires investigation.");
        var variantIds = reservations.Select(x => x.VariantId).Distinct().ToArray();
        var variants = await db.Variants.FromSqlInterpolated($"SELECT * FROM ttb.\"Variants\" WHERE \"Id\" = ANY({variantIds}) ORDER BY \"Id\" FOR UPDATE").ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var group in reservations.GroupBy(x => x.VariantId))
        {
            var reserved = group.Where(x => x.State == ReservationState.Reserved).Sum(x => x.Quantity);
            if (!variants.TryGetValue(group.Key, out var variant) || variant.Reserved < reserved || variant.OnHand - variant.Reserved + reserved < group.Sum(x => x.Quantity))
                throw new CommerceException(409, "insufficient_stock", "Current available stock cannot fulfill this late payment.");
        }
        foreach (var reservation in reservations)
        {
            var reservedDelta = reservation.State == ReservationState.Reserved ? -reservation.Quantity : 0;
            var variant = variants[reservation.VariantId]; variant.OnHand -= reservation.Quantity; variant.Reserved += reservedDelta; reservation.State = ReservationState.Committed;
            db.StockMovements.Add(new StockMovement { VariantId = variant.Id, OrderLineId = reservation.OrderLineId, EffectKey = $"commit:{reservation.OrderLineId}", OnHandDelta = -reservation.Quantity, ReservedDelta = reservedDelta, Actor = actor.Key, Reason = "Finance accepted late credit: " + reason[..Math.Min(400, reason.Length)] });
        }
        var previous = new { order.State, order.PaymentState, attemptState = attempt.State };
        db.Allocations.Add(new PaymentAllocation { BankTransactionId = bank.Id, OrderId = order.Id, AmountMinor = bank.AmountMinor });
        order.PaymentState = PaymentState.Paid; order.State = OrderState.Open; order.ConfirmedAt = clock.GetUtcNow(); attempt.State = AttemptState.Confirmed;
        if (!await db.Jobs.AnyAsync(x => x.EffectKey == $"receipt:{order.Id}", cancellationToken)) db.Jobs.Add(DurableWork.Enqueue($"receipt:{order.Id}", "receipt.ensure", JsonSerializer.Serialize(new { orderId = order.Id })));
        foreach (var line in lines) if (!await db.Jobs.AnyAsync(x => x.EffectKey == $"fulfill:{line.Id}", cancellationToken)) db.Jobs.Add(DurableWork.Enqueue($"fulfill:{line.Id}", "fulfillment.dispatch", JsonSerializer.Serialize(new { orderId = order.Id, lineId = line.Id })));
        db.AuditEvents.Add(new AuditEvent { Actor = actor.Key, Action = "finance.payment-recovery", Resource = $"order:{order.Id}", DataJson = JsonSerializer.Serialize(new { caseId = review.Id, bankId = bank.Id, bank.InstructionId, bank.AmountMinor, before = previous, reason }) });
    }

    public async Task<AdminOrderPage> OrdersAsync(ShopperActor actor, int limit, Guid? before, string? payment, CancellationToken cancellationToken)
    {
        Operations(actor); PageLimit(limit); var query = db.Orders.AsNoTracking().AsQueryable();
        if (before is not null) query = query.Where(x => x.Id.CompareTo(before.Value) < 0);
        if (payment is not null) { if (!Enum.TryParse<PaymentState>(payment, out var state) || !Enum.IsDefined(state)) throw new CommerceException(400, "filter_invalid", "Choose a valid payment state."); query = query.Where(x => x.PaymentState == state); }
        var rows = await query.OrderByDescending(x => x.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        var items = rows.Take(limit).Select(x => new AdminOrderSummary(x.Id, x.Number, x.State.ToString(), x.PaymentState.ToString(),
            JsonSerializer.Deserialize<BuyerDeliverySnapshot>(x.BuyerSnapshotJson, CatalogService.Json)!.Buyer.Name, x.TotalMinor.ToString(CultureInfo.InvariantCulture), x.CreatedAt)).ToArray();
        return new(items, rows.Length > limit ? items[^1].Id : null);
    }

    public async Task<FinanceBankView[]> OrderCreditsAsync(Guid orderId, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); var rows = await (from credit in db.BankTransactions.AsNoTracking() join attempt in db.Attempts.AsNoTracking() on credit.QrId equals attempt.QrId
                                       where attempt.OrderId == orderId && credit.Environment == commerce.Environment && credit.MerchantId == Merchant && credit.BillerNo == Biller && attempt.MerchantId == Merchant
                                       orderby credit.Id select credit).Take(100).ToArrayAsync(cancellationToken);
        var ids = rows.Select(x => x.Id).ToArray();
        var allocations = await db.Allocations.AsNoTracking().Where(x => ids.Contains(x.BankTransactionId)).ToDictionaryAsync(x => x.BankTransactionId, cancellationToken);
        var amounts = await db.ManualRefunds.AsNoTracking().Where(x => ids.Contains(x.BankTransactionId) && x.State != "Cancelled")
            .GroupBy(x => x.BankTransactionId).Select(g => new { Id = g.Key,
                Attested = g.Sum(x => x.State == "Attested" ? (decimal)x.AmountMinor : 0),
                Reserved = g.Sum(x => x.State != "Attested" ? (decimal)x.AmountMinor : 0) }).ToDictionaryAsync(x => x.Id, cancellationToken);
        return rows.Select(row => new FinanceBankView(row.Id, row.InstructionId, row.QrId, row.BillerNo, row.Ref1, row.Ref2,
            row.AmountMinor.ToString(CultureInfo.InvariantCulture), row.Voided, row.CrossBorderFlag, row.CurrencyCode, row.PaymentOccurredAt,
            row.PaymentTimeSource, allocations.GetValueOrDefault(row.Id)?.OrderId, allocations.GetValueOrDefault(row.Id)?.Primary,
            (amounts.GetValueOrDefault(row.Id)?.Attested ?? 0).ToString("0", CultureInfo.InvariantCulture),
            (amounts.GetValueOrDefault(row.Id)?.Reserved ?? 0).ToString("0", CultureInfo.InvariantCulture))).ToArray();
    }
    private async Task<FinanceBankView?> BankAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.BankTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Environment == commerce.Environment && x.MerchantId == Merchant && x.BillerNo == Biller, cancellationToken);
        if (row is null) return null;
        var allocation = await db.Allocations.AsNoTracking().SingleOrDefaultAsync(x => x.BankTransactionId == id, cancellationToken);
        var attested = await db.ManualRefunds.Where(x => x.BankTransactionId == id && x.State == "Attested").SumAsync(x => (decimal)x.AmountMinor, cancellationToken);
        var reserved = await db.ManualRefunds.Where(x => x.BankTransactionId == id && x.State != "Attested" && x.State != "Cancelled").SumAsync(x => (decimal)x.AmountMinor, cancellationToken);
        return new(row.Id, row.InstructionId, row.QrId, row.BillerNo, row.Ref1, row.Ref2, row.AmountMinor.ToString(CultureInfo.InvariantCulture), row.Voided, row.CrossBorderFlag, row.CurrencyCode,
            row.PaymentOccurredAt, row.PaymentTimeSource, allocation?.OrderId, allocation?.Primary, attested.ToString("0", CultureInfo.InvariantCulture), reserved.ToString("0", CultureInfo.InvariantCulture));
    }
    private async Task<CaseSummary> SummaryAsync(ReconciliationCase row, CancellationToken cancellationToken)
    {
        var number = row.OrderId is null ? null : await db.Orders.Where(x => x.Id == row.OrderId).Select(x => x.Number).SingleOrDefaultAsync(cancellationToken);
        var bank = row.BankTransactionId is null ? null : await db.BankTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.BankTransactionId, cancellationToken);
        var manualAmount = await db.ManualRefunds.AsNoTracking().Where(x => x.CaseId == row.Id).Select(x => (long?)x.AmountMinor).SingleOrDefaultAsync(cancellationToken);
        return new(row.Id, row.Version, row.Kind, row.State, row.OrderId, number, row.BankTransactionId, (manualAmount ?? bank?.AmountMinor)?.ToString(CultureInfo.InvariantCulture), bank?.InstructionId, row.Assignee, row.CreatedAt, row.ResolvedAt);
    }
    private static void Finance(ShopperActor actor) { if (!actor.HasRole("finance")) throw new CommerceException(403, "role_required", "Finance permission is required."); }
    public static void Operations(ShopperActor actor) { if (!actor.HasRole("finance") && !actor.HasRole("fulfillment")) throw new CommerceException(403, "role_required", "Store operations permission is required."); }
    private static void PageLimit(int limit) { if (limit is < 1 or > 100) throw new CommerceException(400, "page_invalid", "Page size must be1–100."); }
    private static CommerceException Missing() => new(404, "not_found", "The finance case was not found.");
}
