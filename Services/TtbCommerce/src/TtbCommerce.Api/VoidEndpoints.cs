using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class VoidEndpoints
{
    public static void MapVoidEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/admin/bank-transactions/{id:guid}/void-review", async (Guid id, HttpContext http, ShopperAccess access, VoidService service) =>
        {
            var actor = await access.RequireAsync(http, role: "finance"); return Results.Ok(await service.InspectBankAsync(id, actor, http.RequestAborted));
        });
        routes.MapPost("/admin/bank-transactions/{id:guid}/void", async (Guid id, VoidInput request, HttpContext http, ShopperAccess access, VoidService service) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "finance"); return Results.Accepted(value: await service.RequestBankAsync(id, request, actor, http.RequestAborted));
        });
        routes.MapPost("/admin/orders/{id:guid}/void", async (Guid id, VoidInput request, HttpContext http, ShopperAccess access, VoidService service) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "finance"); return Results.Accepted(value: await service.RequestAsync(id, request, actor, http.RequestAborted));
        });
        routes.MapGet("/admin/voids/{id:guid}", async (Guid id, HttpContext http, ShopperAccess access, VoidService service) =>
        {
            var actor = await access.RequireAsync(http, role: "finance"); return Results.Ok(await service.GetAsync(id, actor, http.RequestAborted));
        });
        routes.MapPost("/admin/voids/{id:guid}/enquiry", async (Guid id, HttpContext http, ShopperAccess access, VoidService service) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "finance"); return Results.Ok(await service.EnquireAsync(id, actor, http.RequestAborted));
        });
        routes.MapGet("/admin/orders/{id:guid}/voids", async (Guid id, HttpContext http, ShopperAccess access, CommerceDb db, VoidOptions policy) =>
        {
            await access.RequireAsync(http, role: "finance");
            return Results.Ok(await db.Voids.AsNoTracking().Where(x => x.OrderId == id).OrderByDescending(x => x.Id)
                .Select(x => new VoidView(x.Id, x.OrderId, x.TransactionId, x.State, x.ResultCode, x.EnquiryCount, Math.Max(0, policy.EnquiryBudget - x.EnquiryCount), x.CreatedAt, x.ConfirmedAt, x.ConfirmationSource)).ToArrayAsync(http.RequestAborted));
        });
    }
}
