using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

// This allowlist grants payment observation only, never catalog/finance mutation rights.
public sealed class PaymentDashboardOptions
{
    public bool Enabled { get; set; }
    public string[] Subjects { get; set; } = [];
    public bool CanRead(ShopperActor actor) => actor.HasRole("finance") || Subjects.Contains(actor.Key, StringComparer.Ordinal);
}

public static class PaymentDashboardEndpoints
{
    public const string Root = "/mangoxttb/dashboard";

    public static void Guard(CommerceOptions commerce, PaymentDashboardOptions dashboard)
    {
        if (!dashboard.Enabled || commerce.Environment == CommerceEnvironment.Production || commerce.Role is not (RuntimeRole.Api or RuntimeRole.All))
            throw new CommerceException(404, "not_found", "The dashboard is unavailable.");
    }

    public static async Task<ShopperActor> RequireAsync(HttpContext http, ShopperAccess access, CommerceOptions commerce,
        PaymentDashboardOptions dashboard, bool mutation = false)
    {
        Guard(commerce, dashboard);
        var actor = await access.RequireAsync(http, mutation);
        if (!dashboard.CanRead(actor)) throw new CommerceException(403, "dashboard_access_required", "This account does not have payment dashboard access.");
        return actor;
    }

    public static void MapPaymentDashboard(this WebApplication app)
    {
        var routes = app.MapGroup(Root + "/api").RequireRateLimiting("dashboard");
        routes.MapGet("/session", async (HttpContext http, ShopperAccess access, CommerceOptions commerce, PaymentDashboardOptions dashboard) =>
        {
            Guard(commerce, dashboard);
            var session = await access.ResolveAsync(http);
            if (session is null) return Results.Ok(new { authenticated = false });
            var csrf = http.Request.Cookies[access.CsrfCookie];
            if (csrf is null || !SecretTokens.Matches(csrf, session.Session.CsrfHash)) return Results.Ok(new { authenticated = false });
            return Results.Ok(new { authenticated = true, canRead = dashboard.CanRead(session.Actor), displayName = session.Actor.DisplayName,
                csrfToken = csrf, environment = commerce.Environment.ToString() });
        });
        routes.MapPost("/session", async (HttpContext http, ShopperAccess access, SessionService sessions,
            CommerceOptions commerce, PaymentDashboardOptions dashboard) =>
        {
            Guard(commerce, dashboard); access.AssertOrigin(http);
            var issued = await sessions.ExchangeAsync(access.Credentials(http), http.Request.Cookies[access.SessionCookie], http.RequestAborted);
            access.WriteCookies(http, issued);
            return Results.Ok(new { authenticated = true, canRead = dashboard.CanRead(issued.Actor), displayName = issued.Actor.DisplayName,
                csrfToken = issued.CsrfToken, environment = commerce.Environment.ToString() });
        }).RequireRateLimiting("session");
        routes.MapPost("/logout", async (HttpContext http, ShopperAccess access, SessionService sessions,
            CommerceOptions commerce, PaymentDashboardOptions dashboard) =>
        {
            Guard(commerce, dashboard); await access.RequireAsync(http, mutation: true);
            await sessions.RevokeAsync((await access.ResolveAsync(http))!.Session.Id, http.RequestAborted);
            access.ClearCookies(http); return Results.NoContent();
        });
        routes.MapGet("/overview", async (string? search, string? state, Guid? before, HttpContext http, ShopperAccess access,
            CommerceOptions commerce, PaymentDashboardOptions dashboard, PaymentDashboardQueries queries) =>
        {
            await RequireAsync(http, access, commerce, dashboard);
            return Results.Ok(await queries.OverviewAsync(search, state, before, http.RequestAborted));
        });
        routes.MapGet("/orders/{id:guid}", async (Guid id, HttpContext http, ShopperAccess access, CommerceOptions commerce,
            PaymentDashboardOptions dashboard, PaymentDashboardQueries queries) =>
        {
            await RequireAsync(http, access, commerce, dashboard);
            return Results.Ok(await queries.OrderAsync(id, http.RequestAborted));
        });
        routes.MapGet("/notifications", async (string? search, Guid? before, HttpContext http, ShopperAccess access,
            CommerceOptions commerce, PaymentDashboardOptions dashboard, PaymentDashboardQueries queries) =>
        {
            await RequireAsync(http, access, commerce, dashboard);
            return Results.Ok(await queries.NotificationsAsync(search, before, http.RequestAborted));
        });
        routes.MapGet("/tests", async (Guid? before, Guid? caseId, HttpContext http, ShopperAccess access, CommerceOptions commerce,
            PaymentDashboardOptions dashboard, BankTestConsoleService tests) =>
        {
            await RequireAsync(http, access, commerce, dashboard);
            return Results.Ok(new { cases = await tests.CasesAsync(50, http.RequestAborted),
                events = await tests.EventsAsync(caseId, null, before, 50, http.RequestAborted) });
        });

        // ASP.NET routing ignores a trailing slash. A separate Root redirect
        // would also match Root + "/" and redirect forever. The catch-all
        // serves index.html for either spelling; asset URLs are absolute.
        app.MapGet(Root + "/{**asset}", (string? asset, HttpContext http, CommerceOptions commerce,
            PaymentDashboardOptions dashboard, IWebHostEnvironment environment) =>
        {
            Guard(commerce, dashboard);
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            var relative = string.IsNullOrEmpty(asset) ? "index.html" : asset;
            if (relative != "index.html" && !System.Text.RegularExpressions.Regex.IsMatch(relative, @"\Aassets/[A-Za-z0-9_-]+\.(js|css|ttf|woff2|svg)\z")) return Results.NotFound();
            var path = Path.Combine(environment.ContentRootPath, "Dashboard", relative);
            if (!File.Exists(path)) return Results.NotFound();
            var contentType = Path.GetExtension(path) switch { ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8", ".ttf" => "font/ttf", ".woff2" => "font/woff2", ".svg" => "image/svg+xml", _ => "application/octet-stream" };
            return Results.File(path, contentType);
        });
    }
}
