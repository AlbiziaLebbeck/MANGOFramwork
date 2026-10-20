using Microsoft.AspNetCore.Mvc;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class DigitalEndpoints
{
    public static void MapDigitalEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapPost("/admin/entitlements/{id:guid}/revoke", async (Guid id, RevokeEntitlementInput input, HttpContext http, ShopperAccess access, EntitlementService inventory) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "finance"); await inventory.RevokeAsync(id, input, actor.Key, http.RequestAborted); return Results.NoContent();
        });
        routes.MapGet("/inventory", async (int? limit, Guid? cursor, HttpContext http, ShopperAccess access, EntitlementService inventory) =>
        {
            var actor = await access.RequireAsync(http); return Results.Ok(await inventory.ListAsync(actor.ShopperId, limit ?? 50, cursor, http.RequestAborted));
        });
        routes.MapGet("/inventory/{id:guid}/download", async (Guid id, HttpContext http, ShopperAccess access, EntitlementService inventory) =>
        {
            var actor = await access.RequireAsync(http); var file = await inventory.DownloadAsync(actor.ShopperId, id, http.TraceIdentifier, http.RequestAborted);
            return Results.File(file.Stream, "application/octet-stream", file.FileName, enableRangeProcessing: true);
        });
        routes.MapGet("/admin/digital-assets", async (HttpContext http, ShopperAccess access, DigitalAssetService assets) =>
        {
            await access.RequireAsync(http, role: "admin"); return Results.Ok(await assets.ListAsync(http.RequestAborted));
        });
        routes.MapPost("/admin/digital-assets", async (HttpContext http, ShopperAccess access, DigitalAssetService assets) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "admin");
            var name = Uri.UnescapeDataString(http.Request.Headers["X-File-Name"].ToString());
            return Results.Ok(await assets.UploadAsync(http.Request.Body, name, actor.Key, http.RequestAborted));
        }).WithMetadata(new RequestSizeLimitAttribute(104857600));
        routes.MapPost("/game/entitlements/validate", async (GameEntitlementCheck input, HttpContext http, PrivateStorageOptions storage, EntitlementService inventory) =>
        {
            GameServiceAccess.Require(http, storage);
            return Results.Ok(await inventory.ValidateGameAsync(input.ShopperId, input.EntitlementId, http.RequestAborted));
        });
    }
    public sealed record GameEntitlementCheck(Guid ShopperId, Guid EntitlementId);
}
