using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/cart", async (HttpContext http, ShopperAccess access, CartService carts) =>
        {
            var actor = await access.RequireAsync(http);
            return Results.Ok(await carts.GetAsync(actor.ShopperId, http.RequestAborted));
        });
        routes.MapPut("/cart/lines/{variantId:guid}", async (Guid variantId, CartChange input, HttpContext http, ShopperAccess access, CartService carts) =>
        {
            var actor = await access.RequireAsync(http, mutation: true);
            return Results.Ok(await carts.ChangeAsync(actor.ShopperId, variantId, input, http.RequestAborted));
        });
        routes.MapPost("/checkout/quote", async (QuoteInput input, HttpContext http, ShopperAccess access, CartService carts) =>
        {
            var actor = await access.RequireAsync(http, mutation: true);
            return Results.Ok(await carts.QuoteAsync(actor, input, http.RequestAborted));
        });
        routes.MapGet("/checkout/options", async (HttpContext http, ShopperAccess access, CommerceOptions options) =>
        {
            await access.RequireAsync(http);
            return Results.Ok(new { options.PickupEnabled, options.ShippingEnabled, options.ShippingFeeMinor, options.MaxCartLines, options.MaxOrderMinor, currency = "THB" });
        });
    }
}
