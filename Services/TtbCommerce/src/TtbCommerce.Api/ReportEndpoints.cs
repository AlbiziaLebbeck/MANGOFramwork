using Microsoft.EntityFrameworkCore;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class ReportEndpoints
{
    public sealed record ReprocessReport(string Reason);
    public static void MapReportEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/admin/reports", async (Guid? before, HttpContext http, ShopperAccess access, CommerceDb db) =>
        {
            await access.RequireAsync(http, role: "finance");
            var query = db.Reports.AsNoTracking().AsQueryable();
            if (before is not null) query = query.Where(x => x.Id.CompareTo(before.Value) < 0);
            return Results.Ok(await query.OrderByDescending(x => x.Id).Take(50).Select(x => new { x.Id, x.FileName, x.FileDate, x.Kind, x.Revision, x.State, x.FailureCode, x.EncryptedSha256, x.PlaintextSha256, x.ReplayOfId, x.CreatedAt, x.CompletedAt }).ToArrayAsync(http.RequestAborted));
        });
        routes.MapGet("/admin/reports/{id:guid}", async (Guid id, HttpContext http, ShopperAccess access, CommerceDb db) =>
        {
            await access.RequireAsync(http, role: "finance");
            var report = await db.Reports.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.FileName, x.State, x.Revision, x.FailureCode, x.TotalsJson, x.ComparisonJson, x.ParserProfileJson, x.AppliedProfileJson, x.EncryptedSha256, x.PlaintextSha256, x.ReplayOfId }).SingleOrDefaultAsync(http.RequestAborted);
            return report is null ? Results.NotFound() : Results.Ok(new { report, outcomes = await db.ReportRows.Where(x => x.ReportArchiveId == id).GroupBy(x => x.Outcome).Select(x => new { outcome = x.Key, count = x.Count() }).ToArrayAsync(http.RequestAborted) });
        });
        routes.MapPost("/admin/reports/{id:guid}/reprocess", async (Guid id, ReprocessReport request, HttpContext http, ShopperAccess access, ReportService reports) =>
        {
            var actor = await access.RequireAsync(http, mutation: true, role: "finance");
            await reports.ReprocessAsync(id, actor.Key, request.Reason, http.RequestAborted); return Results.Accepted();
        });
    }
}
