using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public static class VoidLedger
{
    // Caller owns the bank-instruction lock and the primary order row lock. No stock or original money evidence is deleted.
    public static async Task ConfirmAsync(CommerceDb db, BankTransaction bank, Order? order, string source, string evidenceReference, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Void confirmation requires its evidence transaction.");
        bank.Voided = true;
        var manualRefunds = await db.ManualRefunds.Where(x => x.BankTransactionId == bank.Id && (x.State == "Attested" || x.SubmittedAt != null)).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var conflictKey = $"manual-bank-void-conflict:{bank.Id}";
        if (manualRefunds.Length > 0 && !await db.Cases.AnyAsync(x => x.EffectKey == conflictKey, cancellationToken))
            db.Cases.Add(new ReconciliationCase { EffectKey = conflictKey, Kind = "ManualRefundBankVoidConflict", OrderId = order?.Id, BankTransactionId = bank.Id,
                EvidenceJson = JsonSerializer.Serialize(new { source, evidenceReference, manualRefundIds = manualRefunds, reason = "Bank void and declared external refund both exist; do not collapse their money evidence" }) });
        var pending = await db.Voids.Where(x => x.BankTransactionId == bank.Id && (x.State == "Requested" || x.State == "Unknown")).ToArrayAsync(cancellationToken);
        foreach (var operation in pending)
        {
            if (operation.State == "Confirmed") continue;
            operation.State = "Confirmed"; operation.ConfirmedAt ??= clock.GetUtcNow(); operation.ConfirmationSource ??= source;
            var review = await db.Cases.SingleOrDefaultAsync(x => x.EffectKey == $"void-review:{operation.Id}" && x.State == "Open", cancellationToken);
            if (review is not null)
            {
                review.State = "Resolved"; review.Version++; review.ResolvedAt = clock.GetUtcNow();
                review.ResolutionJson = JsonSerializer.Serialize(new { reason = "BankVoidConfirmed", source, evidenceReference, voidId = operation.Id });
                db.AuditEvents.Add(new AuditEvent { Actor = "bank-void", Action = "finance.case.evidence-resolved", Resource = $"case:{review.Id}", DataJson = review.ResolutionJson });
            }
        }
        var key = $"void-document:{bank.Id}";
        if (!await db.Jobs.AnyAsync(x => x.EffectKey == key, cancellationToken)) db.Jobs.Add(DurableWork.Enqueue(key, "void-document.ensure", JsonSerializer.Serialize(new { orderId = order?.Id, bankTransactionId = bank.Id, source, evidenceReference, observedAt = clock.GetUtcNow() })));
        // Refunding an extra/unallocated credit must never revoke a legitimate primary payment's goods.
        if (order is null || !await db.Allocations.AnyAsync(x => x.BankTransactionId == bank.Id && x.OrderId == order.Id && x.Primary, cancellationToken)) return;
        order.PaymentState = PaymentState.Voided; order.State = OrderState.Review;
        var grants = await db.Entitlements.Where(x => x.OrderId == order.Id && x.State == EntitlementState.Active).ToArrayAsync(cancellationToken);
        foreach (var grant in grants) { grant.State = EntitlementState.Revoked; grant.RevokedAt = clock.GetUtcNow(); grant.RevocationReason = "Bank evidence confirms original credit was voided"; }
        var dispatches = await db.Fulfillments.Where(x => x.OrderId == order.Id && x.State != FulfillmentState.Delivered && x.State != FulfillmentState.Shipped && x.State != FulfillmentState.Cancelled).ToArrayAsync(cancellationToken);
        foreach (var dispatch in dispatches) { if (dispatch.State != FulfillmentState.Review) dispatch.Version++; dispatch.State = FulfillmentState.Review; dispatch.UpdatedAt = clock.GetUtcNow(); }
    }
}
