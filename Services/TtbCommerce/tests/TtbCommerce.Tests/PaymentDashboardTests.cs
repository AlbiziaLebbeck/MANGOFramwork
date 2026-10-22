using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TtbCommerce.Api;
using TtbCommerce.Core;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class PaymentDashboardTests
{
    private const string Root = PaymentDashboardEndpoints.Root + "/api";
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void Dashboard_allowlist_grants_observation_only_and_production_and_workers_are_closed()
    {
        var options = new PaymentDashboardOptions { Enabled = true, Subjects = ["mangosgo:observer"] };
        var actor = new TtbCommerce.Infrastructure.ShopperActor(Guid.NewGuid(), "mangosgo", "observer", "Operator", []);
        Assert.True(options.CanRead(actor)); Assert.False(actor.HasRole("admin")); Assert.False(actor.HasRole("finance"));
        Assert.False(options.CanRead(actor with { Subject = "outsider" }));
        var commerce = PaymentFixture.Commerce;
        PaymentDashboardEndpoints.Guard(commerce, options);
        options.Enabled = false; Assert.Equal(404, Assert.Throws<CommerceException>(() => PaymentDashboardEndpoints.Guard(commerce, options)).Status);
        options.Enabled = true; commerce.Environment = CommerceEnvironment.Production;
        Assert.Equal(404, Assert.Throws<CommerceException>(() => PaymentDashboardEndpoints.Guard(commerce, options)).Status);
        commerce.Environment = CommerceEnvironment.Uat; commerce.Role = RuntimeRole.ReceiptWorker;
        Assert.Equal(404, Assert.Throws<CommerceException>(() => PaymentDashboardEndpoints.Guard(commerce, options)).Status);
    }

    [PgFact]
    public async Task Dashboard_shell_serves_both_root_spellings_without_redirects_and_assets_remain_scoped()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var factory = new CommerceApiFactory(database.ConnectionString, environment: CommerceEnvironment.Uat);
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Dashboard:Enabled"] = "true"
        })));
        using var client = configured.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new("https://localhost") });
        foreach (var path in new[] { PaymentDashboardEndpoints.Root, PaymentDashboardEndpoints.Root + "/" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
            Assert.Null(response.Headers.Location);
            Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(PaymentDashboardEndpoints.Root + "/appsettings.json")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Root + "/overview")).StatusCode);
    }

    [PgFact]
    public async Task Dashboard_requires_verified_allowlisted_identity_and_csrf_and_revocation_clear_access()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var factory = new CommerceApiFactory(database.ConnectionString, environment: CommerceEnvironment.Uat);
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Dashboard:Enabled"] = "true", ["Dashboard:Subjects:0"] = "mangosgo:observer", ["BankTests:Enabled"] = "true"
        })));
        using var anonymous = configured.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Root + "/overview")).StatusCode);
        using var outsider = configured.CreateClient(new() { BaseAddress = new("https://localhost") });
        outsider.DefaultRequestHeaders.Add("Origin", "https://shop.test"); outsider.DefaultRequestHeaders.Add("Authorization", "Bearer outsider");
        var deniedSession = await outsider.PostAsync(Root + "/session", null);
        Assert.False((await deniedSession.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canRead").GetBoolean());
        foreach (var path in new[] { "/overview", "/notifications", "/tests", "/orders/" + Guid.NewGuid() })
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(Root + path)).StatusCode);
        using var observer = configured.CreateClient(new() { BaseAddress = new("https://localhost") });
        observer.DefaultRequestHeaders.Add("Origin", "https://shop.test"); observer.DefaultRequestHeaders.Add("Authorization", "Bearer observer");
        var session = await (await observer.PostAsync(Root + "/session", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(session.GetProperty("canRead").GetBoolean());
        foreach (var path in new[] { "/overview", "/notifications", "/tests" }) Assert.Equal(HttpStatusCode.OK, (await observer.GetAsync(Root + path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await observer.GetAsync(Root + "/overview?state=forged")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await observer.PostAsync(Root + "/logout", null)).StatusCode);
        observer.DefaultRequestHeaders.Add("X-CSRF-Token", session.GetProperty("csrfToken").GetString());
        observer.DefaultRequestHeaders.Remove("Origin"); observer.DefaultRequestHeaders.Add("Origin", "https://evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await observer.PostAsync(Root + "/logout", null)).StatusCode);
        observer.DefaultRequestHeaders.Remove("Origin"); observer.DefaultRequestHeaders.Add("Origin", "https://shop.test");
        Assert.Equal(HttpStatusCode.NoContent, (await observer.PostAsync(Root + "/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await observer.GetAsync(Root + "/overview")).StatusCode);
        await observer.PostAsync(Root + "/session", null); factory.Identity.Revoked.Add("observer");
        Assert.Equal(HttpStatusCode.Unauthorized, (await observer.GetAsync(Root + "/notifications")).StatusCode);
        await using var check = database.Open(); Assert.Empty(await check.Orders.ToArrayAsync()); Assert.Empty(await check.BankTransactions.ToArrayAsync());
    }

    [PgFact]
    public async Task Dashboard_reads_bank_callback_chain_without_exposing_raw_payloads_or_changing_the_ledger()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        await using var db = database.Open();
        await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, new(EvidenceSource.Callback, "SAFE-REQUEST", PeerIp: "203.148.143.103"), default);
        var queries = new PaymentDashboardQueries(db, TimeProvider.System);
        var summary = Json(await queries.OverviewAsync(fixture.Notice.QrId, "paid", null, default));
        Assert.Equal(1, summary.GetProperty("summary").GetProperty("paid").GetInt32());
        Assert.Equal(fixture.OrderId, Assert.Single(summary.GetProperty("orders").EnumerateArray()).GetProperty("id").GetGuid());
        var notices = Json(await queries.NotificationsAsync(fixture.Notice.InstructionId, null, default));
        var row = Assert.Single(notices.GetProperty("items").EnumerateArray()); Assert.Equal("0000", row.GetProperty("ackCode").GetString());
        Assert.Equal("203.148.143.103", row.GetProperty("peerIp").GetString()); Assert.Equal("SAFE-REQUEST", row.GetProperty("requestUid").GetString());
        var detail = Json(await queries.OrderAsync(fixture.OrderId, default));
        Assert.Equal("Paid", detail.GetProperty("order").GetProperty("paymentState").GetString());
        foreach (var output in new[] { summary.GetRawText(), notices.GetRawText(), detail.GetRawText() })
        {
            Assert.DoesNotContain("9999999999", output); Assert.DoesNotContain("ผู้ชำระทดสอบ", output);
            Assert.DoesNotContain("rawPayload", output); Assert.DoesNotContain("buyerSnapshot", output); Assert.DoesNotContain("payerJson", output);
        }
        Assert.Empty(Json(await queries.OverviewAsync("missing", null, null, default)).GetProperty("orders").EnumerateArray());
        Assert.Empty(Json(await queries.NotificationsAsync(null, row.GetProperty("id").GetGuid(), default)).GetProperty("items").EnumerateArray());
        Assert.Equal(400, (await Assert.ThrowsAsync<CommerceException>(() => queries.OverviewAsync(new('a', 101), null, null, default))).Status);
        Assert.Equal(1, await db.BankTransactions.CountAsync()); Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Equal(1, await db.CallbackReceipts.CountAsync());
    }

    [PgFact]
    public async Task Unmatched_callback_and_pagination_are_visible_without_a_shopper_match()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var db = database.Open();
        for (var i = 0; i < 52; i++) db.Observations.Add(new() { Source = "Callback", Outcome = "Rejected", CorrelationId = "unmatched-" + i,
            MetadataJson = "{\"peerIp\":\"203.148.143.103\",\"secret\":\"DO-NOT-EXPOSE\"}", RawPayload = [1, 2, 3] });
        await db.SaveChangesAsync(); var queries = new PaymentDashboardQueries(db, TimeProvider.System);
        var first = Json(await queries.NotificationsAsync(null, null, default)); Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("DO-NOT-EXPOSE", first.GetRawText());
        var second = Json(await queries.NotificationsAsync(null, first.GetProperty("nextCursor").GetGuid(), default));
        Assert.Equal(2, second.GetProperty("items").GetArrayLength()); Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.All(first.GetProperty("items").EnumerateArray(), row => Assert.Equal(JsonValueKind.Null, row.GetProperty("orderNumber").ValueKind));
    }
}
