using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/admin/issuer-profile", async (HttpContext http, ShopperAccess access, IssuerProfileService profiles) =>
        {
            await access.RequireAsync(http, role: "admin"); return Results.Ok(await profiles.GetAsync(http.RequestAborted));
        });
        routes.MapPut("/admin/issuer-profile", async (IssuerProfileInput input, HttpContext http, ShopperAccess access, IssuerProfileService profiles) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "admin");
            return Results.Ok(await profiles.SaveAsync(input, actor.Key, http.RequestAborted));
        });
        routes.MapGet("/orders/{id:guid}/documents", async (Guid id, HttpContext http, ShopperAccess access, ReceiptService receipts) =>
        {
            var actor = await access.RequireAsync(http); return Results.Ok(await receipts.OwnAsync(actor.ShopperId, id, http.RequestAborted));
        });
        routes.MapGet("/documents/{id:guid}/download", async (Guid id, HttpContext http, ShopperAccess access, ReceiptService receipts) =>
        {
            var actor = await access.RequireAsync(http); var file = await receipts.DownloadAsync(actor.ShopperId, id, actor.Key, http.TraceIdentifier, http.RequestAborted);
            return Results.File(file.Stream, "application/pdf", file.FileName, enableRangeProcessing: true);
        });
        routes.MapGet("/admin/documents/{id:guid}/download", async (Guid id, HttpContext http, ShopperAccess access, ReceiptService receipts) =>
        {
            var actor = await access.RequireAsync(http, role: "finance"); var file = await receipts.DownloadAsync(null, id, actor.Key, http.TraceIdentifier, http.RequestAborted);
            return Results.File(file.Stream, "application/pdf", file.FileName, enableRangeProcessing: true);
        });
        routes.MapGet("/orders/{id:guid}/activity", async (Guid id, HttpContext http, ShopperAccess access, OrderService orders, ReceiptService receipts, FulfillmentService fulfillment, CommerceDb db) =>
        {
            var actor = await access.RequireAsync(http); var order = await orders.GetAsync(actor.ShopperId, id, http.RequestAborted);
            var payment = await (from a in db.Allocations.AsNoTracking() join b in db.BankTransactions.AsNoTracking() on a.BankTransactionId equals b.Id
                                 where a.OrderId == id && a.Primary select new OrderPaymentConfirmation(b.InstructionId, order.ConfirmedAt, b.Voided, b.PaymentOccurredAt != null)).SingleOrDefaultAsync(http.RequestAborted);
            // Ownership was checked above. Expose only approved primary-payment totals and goods quantities; internal evidence stays finance-only.
            var refunds = await db.ManualRefunds.AsNoTracking().Where(x => x.OrderId == id && x.AffectsOrderPayment && x.State == "Attested")
                .Select(x => new { x.AmountMinor, x.ApprovedAt }).ToArrayAsync(http.RequestAborted);
            OrderRefundSummary? refund = null;
            if (refunds.Length > 0)
            {
                var returned = await db.GoodsReturns.AsNoTracking().Where(x => x.OrderId == id).GroupBy(x => x.OrderLineId)
                    .Select(g => new ReturnedLineInput(g.Key, g.Sum(x => x.Quantity))).ToArrayAsync(http.RequestAborted);
                refund = new(refunds.Sum(x => (decimal)x.AmountMinor).ToString("0", CultureInfo.InvariantCulture), "ApprovedManualAttestation",
                    refunds.Max(x => x.ApprovedAt), await db.Cases.AnyAsync(x => x.OrderId == id && x.Kind == "ManualRefundFulfillmentReview" && x.State == "Open", http.RequestAborted), returned);
            }
            return Results.Ok(new OrderActivity(order, await fulfillment.OwnAsync(actor.ShopperId, id, http.RequestAborted), await receipts.OwnAsync(actor.ShopperId, id, http.RequestAborted), payment, refund));
        });
    }
}
