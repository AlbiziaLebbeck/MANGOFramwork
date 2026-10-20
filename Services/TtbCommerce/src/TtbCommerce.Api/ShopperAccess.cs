using Microsoft.AspNetCore.Mvc;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public sealed class ShopperAccess(SessionService sessions, CommerceOptions options)
{
    public string SessionCookie => options.Environment == CommerceEnvironment.Local ? "ttb-local-session" : $"__Host-ttb-session-{options.Environment.ToString().ToLowerInvariant()}";
    public string CsrfCookie => options.Environment == CommerceEnvironment.Local ? "ttb-local-csrf" : $"__Host-ttb-csrf-{options.Environment.ToString().ToLowerInvariant()}";

    public IdentityCredentials Credentials(HttpContext context) => IdentityCredentials.Extract(context.Request.Headers.Authorization.ToString(), context.Request.Headers.Cookie.ToString(), options.MangosCookieNames);

    public void AssertOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length == 0 || !options.TrustedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            throw new CommerceException(403, "untrusted_origin", "The request origin is not allowed.");
    }

    public async Task<ResolvedSession?> ResolveAsync(HttpContext context)
    {
        if (context.Items.TryGetValue(typeof(ResolvedSession), out var cached)) return (ResolvedSession?)cached;
        var session = await sessions.ResolveAsync(context.Request.Cookies[SessionCookie], Credentials(context), context.RequestAborted);
        context.Items[typeof(ResolvedSession)] = session;
        return session;
    }

    public async Task<ShopperActor> RequireAsync(HttpContext context, bool mutation = false, string? role = null)
    {
        var resolved = await ResolveAsync(context) ?? throw new CommerceException(401, "login_required", "Please sign in with MANGOs.");
        if (mutation)
        {
            AssertOrigin(context);
            var csrf = context.Request.Headers["X-CSRF-Token"].ToString();
            if (csrf.Length != 64 || !SecretTokens.Matches(csrf, resolved.Session.CsrfHash))
                throw new CommerceException(403, "csrf_invalid", "Refresh the shopping session and try again.");
        }
        if (role is not null && !resolved.Actor.HasRole(role)) throw new CommerceException(403, "role_required", "This operation requires additional permissions.");
        return resolved.Actor;
    }

    public void WriteCookies(HttpContext context, IssuedSession session)
    {
        var cookie = new CookieOptions { HttpOnly = true, Secure = options.Environment != CommerceEnvironment.Local, SameSite = SameSiteMode.Strict, Path = "/", Expires = session.ExpiresAt, IsEssential = true };
        context.Response.Cookies.Append(SessionCookie, session.Token, cookie);
        context.Response.Cookies.Append(CsrfCookie, session.CsrfToken, cookie);
    }

    public void ClearCookies(HttpContext context)
    {
        var cookie = new CookieOptions { Secure = options.Environment != CommerceEnvironment.Local, SameSite = SameSiteMode.Strict, HttpOnly = true, Path = "/" };
        context.Response.Cookies.Delete(SessionCookie, cookie);
        context.Response.Cookies.Delete(CsrfCookie, cookie);
    }
}

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapPost("/session", async (HttpContext http, ShopperAccess access, SessionService sessions) =>
        {
            access.AssertOrigin(http);
            var issued = await sessions.ExchangeAsync(access.Credentials(http), http.Request.Cookies[access.SessionCookie], http.RequestAborted);
            access.WriteCookies(http, issued);
            return Results.Ok(Public(issued.Actor, issued.CsrfToken, issued.ExpiresAt));
        }).RequireRateLimiting("session");

        routes.MapPost("/session/local", async ([FromBody] LocalSignIn body, HttpContext http, ShopperAccess access, SessionService sessions) =>
        {
            access.AssertOrigin(http);
            var issued = await sessions.LocalAsync(body.Subject ?? "", http.Request.Cookies[access.SessionCookie], http.RequestAborted);
            access.WriteCookies(http, issued);
            return Results.Ok(Public(issued.Actor, issued.CsrfToken, issued.ExpiresAt));
        }).RequireRateLimiting("session");

        routes.MapGet("/session", async (HttpContext http, ShopperAccess access) =>
        {
            var resolved = await access.ResolveAsync(http);
            if (resolved is null) { access.ClearCookies(http); return Results.Ok(new { authenticated = false }); }
            var csrf = http.Request.Cookies[access.CsrfCookie];
            if (csrf is null || !SecretTokens.Matches(csrf, resolved.Session.CsrfHash)) { access.ClearCookies(http); return Results.Ok(new { authenticated = false }); }
            return Results.Ok(Public(resolved.Actor, csrf, resolved.Session.ExpiresAt));
        });

        routes.MapPost("/session/logout", async (HttpContext http, ShopperAccess access, SessionService sessions) =>
        {
            await access.RequireAsync(http, mutation: true);
            var resolved = (await access.ResolveAsync(http))!;
            await sessions.RevokeAsync(resolved.Session.Id, http.RequestAborted);
            access.ClearCookies(http);
            return Results.NoContent();
        });
    }

    private static object Public(ShopperActor actor, string csrf, DateTimeOffset expires) => new
    {
        authenticated = true,
        user = new { id = actor.ShopperId, displayName = actor.DisplayName, roles = actor.Roles },
        csrfToken = csrf, expiresAt = expires
    };
    public sealed record LocalSignIn(string? Subject);
}
