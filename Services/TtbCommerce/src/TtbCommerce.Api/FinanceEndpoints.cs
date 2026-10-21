using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class FinanceEndpoints
{
    public static void MapFinanceEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/admin/reconciliation", async (string? state, string? kind, int? limit, Guid? before, Guid? orderId, HttpContext http, ShopperAccess access, FinanceService finance) =>
        {
            var actor = await access.RequireAsync(http, role: "finance"); return Results.Ok(await finance.CasesAsync(actor, state, kind, limit ?? 50, before, http.RequestAborted, orderId));
        });
        routes.MapGet("/admin/reconciliation/{id:guid}", async (Guid id, HttpContext http, ShopperAccess access, FinanceService finance, ManualRefundService manual) =>
        {
            var actor = await access.RequireAsync(http, role: "finance"); var detail = await finance.CaseAsync(id, actor, http.RequestAborted);
            return Results.Ok(detail with { ManualRefund = await manual.ForCaseAsync(id, actor, http.RequestAborted) });
        });
        routes.MapPost("/admin/reconciliation/{id:guid}/actions", async (Guid id, CaseActionInput input, HttpContext http, ShopperAccess access, FinanceService finance) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "finance"); return Results.Ok(await finance.CaseActionAsync(id, input, actor, http.RequestAborted));
        });
        routes.MapGet("/admin/orders", async (int? limit, Guid? before, string? payment, HttpContext http, ShopperAccess access, FinanceService finance) =>
        {
            var actor = await access.RequireAsync(http); return Results.Ok(await finance.OrdersAsync(actor, limit ?? 50, before, payment, http.RequestAborted));
        });
        routes.MapGet("/admin/orders/{id:guid}", async (Guid id, HttpContext http, ShopperAccess access, CommerceDb db, OrderService orders, FulfillmentService fulfillment, ReceiptService receipts, FinanceService finance) =>
        {
            var actor = await access.RequireAsync(http); FinanceService.Operations(actor);
            var owner = await db.Orders.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.ShopperId).SingleOrDefaultAsync(http.RequestAborted)
                ?? throw new CommerceException(404, "not_found", "The order was not found.");
            var order = await orders.GetAsync(owner, id, http.RequestAborted);
            return Results.Ok(new { order, fulfillment = await fulfillment.OwnAsync(owner, id, http.RequestAborted),
                documents = actor.HasRole("finance") ? await receipts.OwnAsync(owner, id, http.RequestAborted) : [],
                credits = actor.HasRole("finance") ? await finance.OrderCreditsAsync(id, actor, http.RequestAborted) : [] });
        });
        routes.MapGet("/admin/finance/summary", async (HttpContext http, ShopperAccess access, CommerceDb db, CommerceOptions commerce, ReportOptions reports, VoidOptions voids, BankReadiness bank, OperationsOptions operations) =>
        {
            await access.RequireAsync(http, role: "finance");
            var reportsConfigured = reports.Enabled && reports.Missing().Count == 0;
            return Results.Ok(new
            {
                environment = commerce.Environment.ToString(), paymentMode = commerce.PaymentMode.ToString(), paymentsReady = !operations.RecoveryFreeze && bank.MissingForCheckout().Count == 0,
                bankAcceptanceComplete = bank.Missing().Count == 0,
                openCases = await db.Cases.CountAsync(x => x.State == "Open", http.RequestAborted),
                unknownVoids = await db.Voids.CountAsync(x => x.State == "Unknown", http.RequestAborted),
                reportsState = reportsConfigured ? "Configured" : "Unavailable",
                waitingFiles = reportsConfigured ? (int?)await db.ReportFetches.CountAsync(x => x.State == "WaitingForFile", http.RequestAborted) : null,
                voidsAvailable = !operations.RecoveryFreeze && voids.Enabled && voids.ValidPolicy && (commerce.Environment == CommerceEnvironment.Local && commerce.PaymentMode == PaymentMode.Mock ? voids.LocalSimulationEnabled : voids.ProfileApproved)
            });
        });
    }
}
