using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class GoodsDispositionService(CommerceDb db, CommerceOptions commerce, TtbOptions ttb, ManualRefundOptions policy, TimeProvider clock)
{
    private sealed record RefundSource(string Name, bool Full);
    private string Merchant => commerce.PaymentMode == PaymentMode.Mock ? "SIM" : ttb.MerchantId;
    private string Biller => commerce.PaymentMode == PaymentMode.Mock ? "000000000000000" : ttb.BillerNo;
    private bool Approved => policy.GoodsDispositionPolicyApproved && (commerce.Environment != CommerceEnvironment.Local || commerce.PaymentMode == PaymentMode.Mock && policy.LocalSimulationEnabled);

    public async Task<GoodsDispositionView> GetAsync(Guid caseId, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); var review = await ReviewAsync(caseId, cancellationToken);
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == review.OrderId, cancellationToken);
        var bank = await CreditAsync(review, cancellationToken); var source = await SourceAsync(review, bank, order.Id, cancellationToken);
        var lines = await db.OrderLines.AsNoTracking().Where(x => x.OrderId == order.Id).OrderBy(x => x.Id).ToArrayAsync(cancellationToken);
        var returned = await db.GoodsReturns.AsNoTracking().Where(x => x.OrderId == order.Id).GroupBy(x => x.OrderLineId).Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, cancellationToken);
        var dispatches = await db.Fulfillments.AsNoTracking().Where(x => x.OrderId == order.Id).ToDictionaryAsync(x => x.OrderLineId, cancellationToken);
        var grants = await db.Entitlements.AsNoTracking().Where(x => x.OrderId == order.Id).ToArrayAsync(cancellationToken);
        var grantIds = grants.Select(x => x.Id).ToArray(); var gameUsed = (await db.GameTickets.AsNoTracking().Where(x => grantIds.Contains(x.EntitlementId) && x.UsedAt != null).Select(x => x.EntitlementId).Distinct().ToArrayAsync(cancellationToken)).ToHashSet();
        var disposed = (await db.GoodsUnits.AsNoTracking().Where(x => x.OrderId == order.Id).Select(x => new { x.OrderLineId, x.UnitIndex }).ToArrayAsync(cancellationToken)).Select(x => (x.OrderLineId, x.UnitIndex)).ToHashSet();
        var lineViews = new List<GoodsLineView>(); var units = new List<GoodsDigitalUnitView>();
        foreach (var line in lines)
        {
            var snapshot = Snapshot(line); var dispatch = dispatches.GetValueOrDefault(line.Id); var count = returned.GetValueOrDefault(line.Id);
            var remainingUnits = line.Kind == ProductKind.Digital ? Enumerable.Range(0, line.Quantity).Count(unit => !disposed.Contains((line.Id, unit))) : line.Quantity;
            lineViews.Add(new(line.Id, line.Name, line.Kind.ToString(), line.Quantity, dispatch?.State.ToString(), dispatch?.Quantity, snapshot.TrackStock, count,
                snapshot.TrackStock ? Math.Max(0, Math.Min(line.Quantity - count, remainingUnits)) : 0));
            if (line.Kind != ProductKind.Digital) continue;
            foreach (var unit in Enumerable.Range(0, line.Quantity))
            {
                var grant = grants.SingleOrDefault(x => x.OrderLineId == line.Id && x.UnitIndex == unit);
                units.Add(new(line.Id, unit, line.Name, snapshot.DigitalKind.ToString(), grant?.Id, grant?.State.ToString() ?? "NotGranted", disposed.Contains((line.Id, unit)), grant?.DownloadRequestedAt is not null, grant is not null && gameUsed.Contains(grant.Id)));
            }
        }
        return new(caseId, review.Version, order.Id, source.Name, source.Full, Approved, order.PaymentState.ToString(), lineViews.ToArray(), units.ToArray());
    }

    public async Task<GoodsDispositionResult> ActAsync(Guid caseId, GoodsDispositionInput input, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor);
        if (!Guid.TryParseExact(input.OperationId, "D", out var operationId) || input.ExpectedCaseVersion < 1 || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500 || input.Reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) ||
            input.RevokeDigitalUnits is null || input.RevokeDigitalUnits.Length > 4096 || input.Restock is null || input.Restock.Length > 100 || input.Restock.Any(x => x.Quantity <= 0) ||
            input.Restock.Select(x => x.OrderLineId).Distinct().Count() != input.Restock.Length || input.RevokeDigitalUnits.Distinct().Count() != input.RevokeDigitalUnits.Length)
            throw new CommerceException(400, "goods_disposition_invalid", "Provide a stable UUID, current case version, reason and distinct bounded line/unit decisions.");
        var key = operationId.ToString("D"); var hash = SecretTokens.Hash(JsonSerializer.Serialize(new { caseId, input, actor = actor.Key }));
        db.ChangeTracker.Clear(); var lookup = await ReviewAsync(caseId, cancellationToken); var bankLookup = await CreditAsync(lookup, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync("finance-operation:" + key, cancellationToken);
        var prior = await db.FinanceOperations.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == key, cancellationToken);
        if (prior is not null)
        {
            if (prior.Action != "finance.goods-disposition" || prior.RequestHash != hash) throw new CommerceException(409, "operation_conflict", "This operation ID already describes another decision.");
            return JsonSerializer.Deserialize<GoodsDispositionResult>(prior.OutcomeJson, CatalogService.Json)!;
        }
        if (!Approved) throw new CommerceException(503, "goods_policy_unapproved", "The company return/revocation policy is not approved.");
        await LockAsync($"bank:{commerce.Environment}:{Merchant}:{bankLookup.InstructionId}", cancellationToken);
        var order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {lookup.OrderId!.Value} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        var review = (await db.Cases.FromSqlInterpolated($"SELECT * FROM ttb.\"Cases\" WHERE \"Id\" = {caseId} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        if (review.Version != input.ExpectedCaseVersion || review.State != "Open") throw new CommerceException(409, "case_changed", "Reload the current goods review before acting.");
        var bank = await CreditAsync(review, cancellationToken); var source = await SourceAsync(review, bank, order.Id, cancellationToken);
        if (source.Full && !input.CancelUndelivered || !source.Full && input.CancelUndelivered)
            throw new CommerceException(409, "goods_cancellation_scope_invalid", "Full refunds require cancellation of undelivered goods; partial refunds use explicit unit decisions.");
        var lines = await db.OrderLines.Where(x => x.OrderId == order.Id).ToDictionaryAsync(x => x.Id, cancellationToken);
        var snapshots = lines.Values.ToDictionary(x => x.Id, Snapshot);
        var returned = await db.GoodsReturns.Where(x => x.OrderId == order.Id).GroupBy(x => x.OrderLineId).Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, cancellationToken);
        var disposed = (await db.GoodsUnits.Where(x => x.OrderId == order.Id).Select(x => new { x.OrderLineId, x.UnitIndex }).ToArrayAsync(cancellationToken)).Select(x => (x.OrderLineId, x.UnitIndex)).ToHashSet();
        var targets = new HashSet<(Guid Line, int Unit)>();
        foreach (var unit in input.RevokeDigitalUnits)
        {
            if (!lines.TryGetValue(unit.OrderLineId, out var line) || line.Kind != ProductKind.Digital || unit.UnitIndex < 0 || unit.UnitIndex >= line.Quantity || disposed.Contains((line.Id, unit.UnitIndex)))
                throw new CommerceException(409, "goods_unit_invalid", "The digital unit is foreign, out of range or already disposed.");
            targets.Add((line.Id, unit.UnitIndex));
        }
        if (source.Full) foreach (var line in lines.Values.Where(x => x.Kind == ProductKind.Digital)) foreach (var unit in Enumerable.Range(0, line.Quantity)) if (!disposed.Contains((line.Id, unit))) targets.Add((line.Id, unit));
        var grants = await db.Entitlements.Where(x => x.OrderId == order.Id).ToArrayAsync(cancellationToken);
        var selectedIds = grants.Where(x => targets.Contains((x.OrderLineId, x.UnitIndex))).Select(x => x.Id).ToArray();
        if (!input.ConsumedGoodsReviewed && (grants.Any(x => selectedIds.Contains(x.Id) && x.DownloadRequestedAt != null) || await db.GameTickets.AnyAsync(x => selectedIds.Contains(x.EntitlementId) && x.UsedAt != null, cancellationToken)))
            throw new CommerceException(409, "goods_consumed_review_required", "Acknowledge downloaded/used goods under the approved company policy.");
        var restockByLine = input.Restock.ToDictionary(x => x.OrderLineId, x => x.Quantity);
        foreach (var item in input.Restock)
        {
            if (!lines.TryGetValue(item.OrderLineId, out var line) || !snapshots[line.Id].TrackStock || item.Quantity > line.Quantity - returned.GetValueOrDefault(line.Id))
                throw new CommerceException(409, "goods_return_quantity_invalid", "The return exceeds the originally purchased, tracked units less prior returns.");
            if (line.Kind == ProductKind.Digital && item.Quantity > targets.Count(x => x.Line == line.Id)) throw new CommerceException(409, "goods_digital_restock_requires_revocation", "Explicitly select each digital unit whose limited stock will be returned.");
            if (!await db.Reservations.AnyAsync(x => x.OrderLineId == line.Id && x.VariantId == line.VariantId && x.State == ReservationState.Committed && x.Quantity == line.Quantity, cancellationToken))
                throw new CommerceException(409, "goods_stock_commit_missing", "Stock can only be returned against its original committed reservation.");
        }
        var variantIds = input.Restock.Select(x => lines[x.OrderLineId].VariantId).Distinct().Order().ToArray();
        var variants = await db.Variants.FromSqlInterpolated($"SELECT * FROM ttb.\"Variants\" WHERE \"Id\" = ANY({variantIds}) ORDER BY \"Id\" FOR UPDATE").ToDictionaryAsync(x => x.Id, cancellationToken);
        var dispatches = await db.Fulfillments.Where(x => x.OrderId == order.Id).ToDictionaryAsync(x => x.OrderLineId, cancellationToken);
        var before = JsonSerializer.Serialize(new { review.Version, orderState = order.State.ToString(), paymentState = order.PaymentState.ToString(), stock = variants.Values.Select(x => new { x.Id, x.OnHand, x.Reserved }), grants = grants.Select(x => new { x.Id, state = x.State.ToString() }) });
        foreach (var item in input.Restock)
        {
            var line = lines[item.OrderLineId]; var variant = variants[line.VariantId]; var next = (long)variant.OnHand + item.Quantity;
            if (next > int.MaxValue) throw new CommerceException(409, "stock_conflict", "The explicit return would exceed stock bounds.");
            variant.OnHand = (int)next; var effect = $"goods-return:{key}:{line.Id}";
            db.GoodsReturns.Add(new GoodsReturnLine { CaseId = caseId, OrderId = order.Id, OrderLineId = line.Id, Quantity = item.Quantity, EffectKey = effect, Actor = actor.Key, Reason = input.Reason, CreatedAt = clock.GetUtcNow() });
            db.StockMovements.Add(new StockMovement { VariantId = line.VariantId, OrderLineId = line.Id, EffectKey = effect, OnHandDelta = item.Quantity, Actor = actor.Key, Reason = "Approved returned units: " + input.Reason[..Math.Min(400, input.Reason.Length)], CreatedAt = clock.GetUtcNow() });
            if (!source.Full && line.Kind == ProductKind.Physical && dispatches.TryGetValue(line.Id, out var dispatch) && dispatch.State is not (FulfillmentState.Shipped or FulfillmentState.Delivered or FulfillmentState.Cancelled))
            {
                dispatch.Quantity = line.Quantity - returned.GetValueOrDefault(line.Id) - item.Quantity;
                if (dispatch.Quantity == 0) { dispatch.Quantity = line.Quantity; dispatch.State = FulfillmentState.Cancelled; }
                dispatch.Version++; dispatch.UpdatedAt = clock.GetUtcNow();
            }
        }
        foreach (var group in targets.GroupBy(x => x.Line))
        {
            var line = lines[group.Key]; var snapshot = snapshots[line.Id]; var toRestock = restockByLine.GetValueOrDefault(line.Id);
            foreach (var target in group.OrderBy(x => x.Unit))
            {
                var grant = grants.SingleOrDefault(x => x.OrderLineId == line.Id && x.UnitIndex == target.Unit);
                if (grant is null)
                {
                    grant = new Entitlement { ShopperId = order.ShopperId, OrderId = order.Id, OrderLineId = line.Id, ProductId = snapshot.ProductId, VariantId = line.VariantId,
                        UnitIndex = target.Unit, DigitalKind = snapshot.DigitalKind, DigitalAssetKey = snapshot.DigitalAssetKey, Name = line.Name, VariantName = line.VariantName, CreatedAt = clock.GetUtcNow() };
                    db.Entitlements.Add(grant);
                }
                if (grant.ShopperId != order.ShopperId || grant.ProductId != snapshot.ProductId || grant.VariantId != line.VariantId) throw new CommerceException(409, "goods_unit_invalid", "The digital unit does not match its immutable purchase.");
                grant.State = EntitlementState.Revoked; grant.RevokedAt ??= clock.GetUtcNow(); grant.RevocationReason ??= "Approved refund goods decision: " + input.Reason[..Math.Min(350, input.Reason.Length)];
                var restocked = toRestock-- > 0;
                db.GoodsUnits.Add(new GoodsUnitDisposition { CaseId = caseId, OrderId = order.Id, OrderLineId = line.Id, UnitIndex = target.Unit, EntitlementId = grant.Id, Restocked = restocked, Actor = actor.Key, Reason = input.Reason, CreatedAt = clock.GetUtcNow() });
                db.EntitlementOperations.Add(new EntitlementOperation { EntitlementId = grant.Id, OperationKey = $"refund-unit:{key}:{line.Id}:{target.Unit}", RequestHash = hash, Action = "refund-unit-revoke", Actor = actor.Key, Reason = input.Reason, CreatedAt = clock.GetUtcNow() });
            }
        }
        if (!source.Full)
        {
            foreach (var line in lines.Values)
            {
                var allCancelled = line.Kind == ProductKind.Digital
                    ? Enumerable.Range(0, line.Quantity).All(unit => disposed.Contains((line.Id, unit)) || targets.Contains((line.Id, unit)))
                    : returned.GetValueOrDefault(line.Id) + restockByLine.GetValueOrDefault(line.Id) == line.Quantity;
                if (!allCancelled) continue;
                if (dispatches.TryGetValue(line.Id, out var dispatch))
                {
                    if (dispatch.State is FulfillmentState.Shipped or FulfillmentState.Delivered or FulfillmentState.Cancelled) continue;
                    dispatch.State = FulfillmentState.Cancelled; dispatch.Version++; dispatch.UpdatedAt = clock.GetUtcNow();
                }
                else
                {
                    var delivery = JsonSerializer.Deserialize<BuyerDeliverySnapshot>(order.BuyerSnapshotJson, CatalogService.Json)!;
                    var cancelled = new FulfillmentItem { OrderId = order.Id, OrderLineId = line.Id, Kind = line.Kind, Quantity = line.Quantity,
                        DeliveryMethod = line.Kind == ProductKind.Digital ? "digital" : delivery.Delivery.Method, State = FulfillmentState.Cancelled, OperatorNote = input.Reason, UpdatedAt = clock.GetUtcNow(), CreatedAt = clock.GetUtcNow() };
                    db.Fulfillments.Add(cancelled); dispatches.Add(line.Id, cancelled);
                }
            }
        }
        if (source.Full)
        {
            foreach (var line in lines.Values)
            {
                if (dispatches.TryGetValue(line.Id, out var dispatch))
                {
                    if (dispatch.State is FulfillmentState.Shipped or FulfillmentState.Delivered or FulfillmentState.Cancelled) continue;
                    dispatch.State = FulfillmentState.Cancelled; dispatch.Version++; dispatch.UpdatedAt = clock.GetUtcNow(); dispatch.OperatorNote = input.Reason;
                }
                else
                {
                    var delivery = JsonSerializer.Deserialize<BuyerDeliverySnapshot>(order.BuyerSnapshotJson, CatalogService.Json)!;
                    var cancelled = new FulfillmentItem { OrderId = order.Id, OrderLineId = line.Id, Kind = line.Kind, Quantity = line.Quantity, DeliveryMethod = line.Kind == ProductKind.Digital ? "digital" : delivery.Delivery.Method,
                        State = FulfillmentState.Cancelled, OperatorNote = input.Reason, UpdatedAt = clock.GetUtcNow(), CreatedAt = clock.GetUtcNow() };
                    db.Fulfillments.Add(cancelled); dispatches.Add(line.Id, cancelled);
                }
            }
        }
        review.State = "Resolved"; review.Version++; review.ResolvedAt = clock.GetUtcNow();
        review.ResolutionJson = JsonSerializer.Serialize(new { source.Name, source.Full, input, actor = actor.Key, noAutomaticRestock = true });
        if (!await db.Cases.AnyAsync(x => x.OrderId == order.Id && x.State == "Open" && x.Id != caseId, cancellationToken))
        {
            if (order.PaymentState is PaymentState.Voided or PaymentState.Refunded) order.State = OrderState.Cancelled;
            else if (order.PaymentState == PaymentState.Paid) order.State = dispatches.Count == lines.Count && dispatches.Values.All(x => x.State is FulfillmentState.Delivered or FulfillmentState.Cancelled) ? OrderState.Completed : OrderState.Open;
        }
        var operation = new FinanceOperation { CaseId = caseId, OrderId = order.Id, OperationKey = key, RequestHash = hash, Action = "finance.goods-disposition", Actor = actor.Key, Reason = input.Reason, BeforeJson = before, CreatedAt = clock.GetUtcNow() };
        var result = new GoodsDispositionResult(operation.Id, caseId, review.Version, review.State, order.State.ToString(), input.Restock.Length, targets.Count); operation.OutcomeJson = JsonSerializer.Serialize(result, CatalogService.Json); db.FinanceOperations.Add(operation);
        db.CaseNotes.Add(new FinanceCaseNote { CaseId = caseId, Actor = actor.Key, Body = "Goods disposition: " + input.Reason, CreatedAt = clock.GetUtcNow() });
        db.AuditEvents.Add(new AuditEvent { Actor = actor.Key, Action = "finance.goods-disposition", Resource = $"case:{caseId}", DataJson = JsonSerializer.Serialize(new { operationId = operation.Id, before, after = result, input }), CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return result;
    }

    private async Task<RefundSource> SourceAsync(ReconciliationCase review, BankTransaction bank, Guid orderId, CancellationToken cancellationToken)
    {
        if (!await db.Allocations.AnyAsync(x => x.OrderId == orderId && x.BankTransactionId == bank.Id && x.Primary, cancellationToken)) throw new CommerceException(409, "goods_primary_credit_required", "Goods decisions must refer to the order's actual primary payment.");
        if (await db.Cases.AnyAsync(x => x.BankTransactionId == bank.Id && x.Kind == "ManualRefundBankVoidConflict" && x.State == "Open", cancellationToken)) throw new CommerceException(409, "goods_finance_conflict", "Resolve conflicting external/bank refund evidence before disposing of goods.");
        if (review.Kind is "VoidFulfillmentReview" or "ReportedVoidFulfillmentReview")
        {
            if (!bank.Voided) throw new CommerceException(409, "goods_refund_unconfirmed", "Bank void evidence is not confirmed.");
            return new("BankVoidEvidence", true);
        }
        const string prefix = "manual-refund-goods:";
        if (review.Kind != "ManualRefundFulfillmentReview" || !review.EffectKey.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParse(review.EffectKey[prefix.Length..], out var manualId) ||
            !await db.ManualRefunds.AnyAsync(x => x.Id == manualId && x.BankTransactionId == bank.Id && x.OrderId == orderId && x.AffectsOrderPayment && x.State == "Attested", cancellationToken))
            throw new CommerceException(409, "goods_refund_unconfirmed", "An approved external-refund attestation is required.");
        var amount = await db.ManualRefunds.Where(x => x.BankTransactionId == bank.Id && x.State == "Attested").SumAsync(x => (decimal)x.AmountMinor, cancellationToken);
        return new("ApprovedManualAttestation", amount == bank.AmountMinor);
    }
    private async Task<ReconciliationCase> ReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw Missing();
        if (row.OrderId is null || row.BankTransactionId is null || row.Kind is not ("VoidFulfillmentReview" or "ReportedVoidFulfillmentReview" or "ManualRefundFulfillmentReview")) throw new CommerceException(409, "goods_review_required", "Choose a confirmed refund's goods-review case.");
        return row;
    }
    private async Task<BankTransaction> CreditAsync(ReconciliationCase row, CancellationToken cancellationToken) => await db.BankTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.BankTransactionId && x.Environment == commerce.Environment && x.MerchantId == Merchant && x.BillerNo == Biller, cancellationToken) ?? throw Missing();
    private static PurchaseLine Snapshot(OrderLine line)
    {
        var snapshot = JsonSerializer.Deserialize<PurchaseLine>(line.SnapshotJson, CatalogService.Json);
        if (snapshot is null || snapshot.Quantity != line.Quantity || snapshot.Kind != line.Kind || snapshot.VariantId != line.VariantId || line.Kind == ProductKind.Physical && !snapshot.TrackStock) throw new CommerceException(409, "goods_snapshot_invalid", "The immutable purchase needs investigation.");
        return snapshot;
    }
    private Task<int> LockAsync(string key, CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    private void Finance(ShopperActor actor)
    {
        if (!actor.HasRole("finance")) throw new CommerceException(403, "role_required", "Finance permission is required.");
        if (!(commerce.Environment == CommerceEnvironment.Local && commerce.PaymentMode == PaymentMode.Mock || commerce.Environment is CommerceEnvironment.Uat or CommerceEnvironment.Production && commerce.PaymentMode == PaymentMode.Bank)) throw Missing();
    }
    private static CommerceException Missing() => new(404, "not_found", "The goods case was not found.");
}
