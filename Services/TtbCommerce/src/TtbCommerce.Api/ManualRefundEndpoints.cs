using Microsoft.AspNetCore.Mvc;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class ManualRefundEndpoints
{
    public static void MapManualRefundEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/admin/reconciliation/{id:guid}/goods-review", async (Guid id, HttpContext http, ShopperAccess access, GoodsDispositionService service) =>
        {
            var actor = await access.RequireAsync(http, role: "finance"); return Results.Ok(await service.GetAsync(id, actor, http.RequestAborted));
        });
        routes.MapPost("/admin/reconciliation/{id:guid}/goods-disposition", async (Guid id, GoodsDispositionInput input, HttpContext http, ShopperAccess access, GoodsDispositionService service) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "finance"); return Results.Ok(await service.ActAsync(id, input, actor, http.RequestAborted));
        });
        routes.MapPost("/admin/bank-transactions/{id:guid}/manual-refunds", async (Guid id, ManualRefundInput input, HttpContext http, ShopperAccess access, ManualRefundService service) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "finance"); return Results.Accepted(value: await service.CreateAsync(id, input, actor, http.RequestAborted));
        });
        routes.MapGet("/admin/manual-refunds/{id:guid}", async (Guid id, HttpContext http, ShopperAccess access, ManualRefundService service) =>
        {
            var actor = await access.RequireAsync(http, role: "finance"); return Results.Ok(await service.GetAsync(id, actor, http.RequestAborted));
        });
        routes.MapPost("/admin/manual-refunds/{id:guid}/actions", async (Guid id, ManualRefundActionInput input, HttpContext http, ShopperAccess access, ManualRefundService service) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "finance"); return Results.Ok(await service.ActAsync(id, input, actor, http.RequestAborted));
        });
        routes.MapPost("/admin/reconciliation/{id:guid}/evidence", async (Guid id, HttpContext http, ShopperAccess access, FinanceEvidenceService evidence) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "finance");
            string name; try { name = Uri.UnescapeDataString(http.Request.Headers["X-File-Name"].ToString()); } catch (UriFormatException) { throw new CommerceException(400, "finance_evidence_file_invalid", "Provide an encoded evidence filename."); }
            return Results.Ok(await evidence.UploadAsync(id, http.Request.Body, name, actor, http.RequestAborted));
        }).WithMetadata(new RequestSizeLimitAttribute(20 * 1024 * 1024));
        routes.MapGet("/admin/finance-evidence/{id:guid}/download", async (Guid id, HttpContext http, ShopperAccess access, FinanceEvidenceService evidence) =>
        {
            var actor = await access.RequireAsync(http, role: "finance"); var file = await evidence.DownloadAsync(id, actor, http.TraceIdentifier, http.RequestAborted);
            return Results.File(file.Stream, "application/octet-stream", file.FileName, enableRangeProcessing: false);
        });
    }
}
