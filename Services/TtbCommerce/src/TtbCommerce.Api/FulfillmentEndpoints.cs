using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class FulfillmentEndpoints
{
    public static void MapFulfillmentEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/orders/{id:guid}/fulfillment", async (Guid id, HttpContext http, ShopperAccess access, FulfillmentService fulfillment) =>
        {
            var actor = await access.RequireAsync(http); return Results.Ok(await fulfillment.OwnAsync(actor.ShopperId, id, http.RequestAborted));
        });
        routes.MapGet("/admin/fulfillment", async (int? limit, Guid? cursor, HttpContext http, ShopperAccess access, FulfillmentService fulfillment) =>
        {
            await access.RequireAsync(http, role: "fulfillment"); return Results.Ok(await fulfillment.QueueAsync(limit ?? 50, cursor, http.RequestAborted));
        });
        routes.MapPost("/admin/fulfillment/{id:guid}/actions", async (Guid id, FulfillmentActionInput input, HttpContext http, ShopperAccess access, FulfillmentService fulfillment) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "fulfillment"); return Results.Ok(await fulfillment.ActAsync(id, input, actor.Key, http.RequestAborted));
        });
    }
}
