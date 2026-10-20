using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapPost("/orders/{id:guid}/mock-pay", async (Guid id, MockPaymentInput input, HttpContext http, ShopperAccess access, MockBankService simulator) =>
        {
            var actor = await access.RequireAsync(http, mutation: true);
            return Results.Ok(await simulator.PayAsync(actor.ShopperId, id, input, http.RequestAborted));
        });
        routes.MapPost("/orders/{id:guid}/verify", async (Guid id, VerificationInput input, HttpContext http, ShopperAccess access, VerificationService verification) =>
        {
            var actor = await access.RequireAsync(http, mutation: true);
            return Results.Ok(await verification.CheckAsync(actor.ShopperId, id, input, http.RequestAborted));
        });
        routes.MapPost("/orders/{id:guid}/qr", async (Guid id, HttpContext http, ShopperAccess access, QrService qr) =>
        {
            var actor = await access.RequireAsync(http, mutation: true);
            return Results.Ok(await qr.GetOrCreateAsync(actor.ShopperId, id, http.RequestAborted));
        });
        routes.MapPost("/orders", async (CreateOrderInput input, HttpContext http, ShopperAccess access, OrderService orders) =>
        {
            var actor = await access.RequireAsync(http, mutation: true);
            return Results.Ok(await orders.CreateAsync(actor.ShopperId, input, http.Request.Headers["Idempotency-Key"].ToString(), http.RequestAborted));
        });
        routes.MapGet("/orders", async (int? limit, Guid? cursor, HttpContext http, ShopperAccess access, OrderService orders) =>
        {
            var actor = await access.RequireAsync(http);
            return Results.Ok(await orders.ListAsync(actor.ShopperId, limit ?? 20, cursor, http.RequestAborted));
        });
        routes.MapGet("/orders/{id:guid}", async (Guid id, HttpContext http, ShopperAccess access, OrderService orders) =>
        {
            var actor = await access.RequireAsync(http);
            return Results.Ok(await orders.GetAsync(actor.ShopperId, id, http.RequestAborted));
        });
        routes.MapPost("/orders/{id:guid}/cancel", async (Guid id, HttpContext http, ShopperAccess access, OrderService orders) =>
        {
            var actor = await access.RequireAsync(http, mutation: true);
            await orders.CancelAsync(actor.ShopperId, id, http.RequestAborted);
            return Results.Ok(await orders.GetAsync(actor.ShopperId, id, http.RequestAborted));
        });
    }
}
