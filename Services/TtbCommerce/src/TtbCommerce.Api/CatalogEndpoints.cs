using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/catalog/products", (int? limit, Guid? cursor, CatalogService catalog, CancellationToken cancellationToken) => catalog.ListAsync(limit ?? 24, cursor, cancellationToken));
        routes.MapGet("/catalog/products/{id:guid}", (Guid id, CatalogService catalog, CancellationToken cancellationToken) => catalog.GetAsync(id, cancellationToken));
        routes.MapGet("/catalog/by-model/{modelKey}", (string modelKey, CatalogService catalog, CancellationToken cancellationToken) => catalog.ByModelAsync(modelKey, cancellationToken));
        routes.MapGet("/admin/catalog", async (int? limit, Guid? before, HttpContext http, ShopperAccess access, CatalogService catalog) =>
        {
            await access.RequireAsync(http, role: "admin");
            return Results.Ok(await catalog.AdminListAsync(http.RequestAborted, limit ?? 100, before));
        });
        routes.MapGet("/admin/catalog/{id:guid}", async (Guid id, HttpContext http, ShopperAccess access, CatalogService catalog) =>
        {
            await access.RequireAsync(http, role: "admin"); return Results.Ok(await catalog.AdminGetAsync(id, http.RequestAborted));
        });
        routes.MapPost("/admin/products", async (ProductDraftInput input, HttpContext http, ShopperAccess access, CatalogService catalog) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "admin");
            var product = await catalog.CreateAsync(input, actor.Key, http.RequestAborted);
            return Results.Ok(product);
        });
        routes.MapPut("/admin/products/{id:guid}", async (Guid id, ProductEditInput input, HttpContext http, ShopperAccess access, CatalogService catalog) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "admin");
            return Results.Ok(await catalog.EditAsync(id, input, actor.Key, http.RequestAborted));
        });
        routes.MapPost("/admin/products/{id:guid}/variants", async (Guid id, VariantInput input, HttpContext http, ShopperAccess access, CatalogService catalog) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "admin");
            return Results.Ok(await catalog.WriteVariantAsync(id, null, input, actor.Key, http.RequestAborted));
        });
        routes.MapPut("/admin/products/{id:guid}/variants/{variantId:guid}", async (Guid id, Guid variantId, VariantInput input, HttpContext http, ShopperAccess access, CatalogService catalog) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "admin");
            return Results.Ok(await catalog.WriteVariantAsync(id, variantId, input, actor.Key, http.RequestAborted));
        });
        routes.MapPost("/admin/products/{id:guid}/publication", async (Guid id, PublicationInput input, HttpContext http, ShopperAccess access, CatalogService catalog) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "admin");
            return Results.Ok(await catalog.PublishAsync(id, input, actor.Key, http.RequestAborted));
        });
        routes.MapPost("/admin/variants/{id:guid}/stock", async (Guid id, StockAdjustmentInput input, HttpContext http, ShopperAccess access, CatalogService catalog) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "admin");
            return Results.Ok(await catalog.AdjustStockAsync(id, input, actor.Key, http.RequestAborted));
        });
    }
}
