using System.Net;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class BankTestConsoleEndpoints
{
    private const string Root = "/internal/bank-test-console";
    private const string SocketKey = "ttb-bank-test-original-loopback";

    // Capture the socket before forwarded headers. An internet/proxy caller
    // cannot become a console operator by claiming X-Forwarded-For:127.0.0.1.
    public static void UseBankTestConsoleSocket(this WebApplication app) => app.Use(async (http, next) =>
    {
        if (http.Request.Path.StartsWithSegments(Root))
        {
            var peer = http.Connection.RemoteIpAddress;
            if (peer?.IsIPv4MappedToIPv6 == true) peer = peer.MapToIPv4();
            http.Items[SocketKey] = peer is not null && IPAddress.IsLoopback(peer);
        }
        await next(http);
    });

    public static void Guard(HttpContext http, BankTestConsoleService tests, bool mutation = false)
    {
        tests.Guard();
        var host = http.Request.Host.Host;
        var localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
        if (http.Items[SocketKey] is not true || !localHost)
            throw new CommerceException(403, "bank_test_private_console", "Use the private SSH loopback tunnel for the bank test console.");
        if (mutation)
        {
            var origin = http.Request.Headers.Origin.ToString();
            if (http.Request.Headers["X-Bank-Test-Action"] != "1" ||
                !string.IsNullOrEmpty(origin) && !origin.Equals($"{http.Request.Scheme}://{http.Request.Host}", StringComparison.OrdinalIgnoreCase))
                throw new CommerceException(403, "bank_test_origin_rejected", "A same-origin console action is required.");
        }
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    public static void MapBankTestConsole(this WebApplication app)
    {
        app.MapGet(Root + "/", (HttpContext http, BankTestConsoleService tests) =>
        {
            Guard(http, tests);
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
            return Results.Stream(typeof(BankTestConsoleEndpoints).Assembly.GetManifestResourceStream("TtbCommerce.Api.BankTestConsole.html")!, "text/html; charset=utf-8");
        });
        app.MapGet(Root + "/font.ttf", (HttpContext http, BankTestConsoleService tests) =>
        {
            Guard(http, tests); return Results.Stream(typeof(PrivateFiles).Assembly.GetManifestResourceStream("TtbCommerce.Infrastructure.Fonts.NotoSansThai.ttf")!, "font/ttf");
        });
        app.MapGet(Root + "/api/config", (HttpContext http, BankTestConsoleService tests, BankTestOptions options, TtbOptions bank, CommerceOptions commerce) =>
        {
            Guard(http, tests); return Results.Ok(new { environment = "Uat", options.Reference1, options.MaximumAmountMinor,
                options.ReadBudget, options.VoidEnabled, commerce.VerificationDelaySeconds, bank.BeneficiaryName, bank.MerchantId, bank.BillerNo });
        });
        app.MapGet(Root + "/api/cases", async (int? limit, HttpContext http, BankTestConsoleService tests) =>
        { Guard(http, tests); return Results.Ok(await tests.CasesAsync(limit ?? 50, http.RequestAborted)); });
        app.MapGet(Root + "/api/cases/{id:guid}", async (Guid id, HttpContext http, BankTestConsoleService tests) =>
        { Guard(http, tests); return Results.Ok(await tests.CaseAsync(id, http.RequestAborted)); });
        app.MapPost(Root + "/api/cases", async (BankTestCreateInput input, HttpContext http, BankTestConsoleService tests) =>
        { Guard(http, tests, true); return Results.Ok(await tests.CreateAsync(input, http.RequestAborted)); });
        app.MapGet(Root + "/api/events", async (Guid? caseId, string? kind, Guid? before, int? limit, HttpContext http, BankTestConsoleService tests) =>
        { Guard(http, tests); return Results.Ok(await tests.EventsAsync(caseId, kind, before, limit ?? 50, http.RequestAborted)); });
        app.MapGet(Root + "/api/cases/{id:guid}/operations", async (Guid id, HttpContext http, BankTestConsoleService tests) =>
        { Guard(http, tests); return Results.Ok(await tests.OperationsAsync(id, http.RequestAborted)); });
        app.MapPost(Root + "/api/cases/{id:guid}/actions/{kind}", async (Guid id, string kind, BankTestActionInput input, HttpContext http, BankTestConsoleService tests) =>
        { Guard(http, tests, true); return Results.Ok(await tests.ActionAsync(id, kind, input, http.RequestAborted)); });
    }
}
