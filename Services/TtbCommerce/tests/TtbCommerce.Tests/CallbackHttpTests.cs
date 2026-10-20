using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class BankCallbackApiFactory(string connection, bool approved = true) : CommerceApiFactory(connection, environment: CommerceEnvironment.Uat)
{
    public IPAddress Peer { get; set; } = IPAddress.Parse("203.148.143.101");
    public string[] TrustedProxyCidrs { get; set; } = [];
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Ttb:MerchantId"] = "TST", ["Ttb:BillerNo"] = "111111111111111",
                ["Ttb:CallbackProfileApproved"] = approved.ToString(), ["Ttb:CallbackSourceIpValidated"] = approved.ToString(),
                ["Ttb:CallbackAuthentication"] = "Basic", ["Ttb:CallbackUsername"] = "unit-test-callback", ["Ttb:CallbackPassword"] = "unit-test-only-not-a-bank-secret"
            };
            for (var index = 0; index < TrustedProxyCidrs.Length; index++) settings[$"Ttb:TrustedProxyCidrs:{index}"] = TrustedProxyCidrs[index];
            config.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new TestPeerFilter(() => Peer)));
    }
    private sealed class TestPeerFilter(Func<IPAddress> peer) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) => { context.Connection.RemoteIpAddress = peer(); return continuation(context); });
            next(app);
        };
    }
}

public sealed class CallbackHttpTests
{
    [PgFact]
    public async Task Open_outgoing_service_circuits_do_not_block_authenticated_credit_commit_or_duplicate_ACKs()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        await using var factory = new BankCallbackApiFactory(database.ConnectionString); using var client = factory.ShopperClient(); Basic(client);
        var circuit = factory.Services.GetRequiredService<BankCircuitBreaker>();
        foreach (var operation in new[] { BankOperation.Verify, BankOperation.EnquiryRange, BankOperation.MerchantVoid, BankOperation.EnquiryVoid })
            for (var index = 0; index < 5; index++) circuit.Complete(Assert.IsType<BankCircuitBreaker.Permit>(circuit.TryEnter(operation)), true);
        Assert.All(circuit.Read(), state => Assert.True(state.Open));
        var response = await SendAsync(client, fixture.Raw); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("0000", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ResCode").GetString());
        var duplicate = await SendAsync(client, fixture.Raw);
        Assert.Equal("0004", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ResCode").GetString());
        await using var check = database.Open(); Assert.Equal(1, await check.BankTransactions.CountAsync()); Assert.Equal(1, await check.Allocations.CountAsync());
        Assert.Equal(PaymentState.Paid, (await check.Orders.SingleAsync()).PaymentState);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, byte[] raw)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/ttb/v1/credit-notification") { Content = new ByteArrayContent(raw) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("Service-Name", "credit-notification"); request.Headers.Add("Request-Datetime", "2026-10-18T10:26:03.489+07:00");
        return client.SendAsync(request);
    }

    private static void Basic(HttpClient client) => client.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("unit-test-callback:unit-test-only-not-a-bank-secret")));

    [PgFact]
    public async Task Approved_callback_requires_both_bank_transport_and_credentials_and_emits_bank_ACK_shape()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        await using var factory = new BankCallbackApiFactory(database.ConnectionString); using var client = factory.ShopperClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, fixture.Raw)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "alice"); Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, fixture.Raw)).StatusCode);
        Basic(client); factory.Peer = IPAddress.Parse("203.0.113.20");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.148.143.101");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, fixture.Raw)).StatusCode);
        factory.Peer = IPAddress.Parse("2001:db8::cb94:8f65"); // same low32bits as a bank IPv4 address must not become that IPv4 source
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, fixture.Raw)).StatusCode);
        await using (var db = database.Open()) Assert.Equal(0, await db.Observations.CountAsync());
        factory.Peer = IPAddress.Parse("::ffff:203.148.143.101");
        var response = await SendAsync(client, fixture.Raw); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("0000", json.GetProperty("ResCode").GetString()); Assert.Equal(fixture.Notice.InstructionId, json.GetProperty("InstructionId").GetString());
        Assert.False(json.TryGetProperty("resCode", out _)); Assert.Equal("credit-notification", response.Headers.GetValues("Service-Name").Single());
        await using (var db = database.Open())
        {
            Assert.Equal(1, await db.Allocations.CountAsync());
            Assert.DoesNotContain("unit-test-only-not-a-bank-secret", (await db.Observations.SingleAsync()).MetadataJson);
        }
    }

    [PgFact]
    public async Task Unapproved_profile_is_closed_and_local_mock_has_no_bank_callback()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using (var factory = new BankCallbackApiFactory(database.ConnectionString, approved: false))
        {
            using var client = factory.ShopperClient(); Basic(client);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await SendAsync(client, Encoding.UTF8.GetBytes("{}"))).StatusCode);
        }
        // A separate disposable DB is needed: environment identity cannot be switched from UAT to Local.
        await using var localDatabase = await PgDatabase.CreateAsync(); await using var localFactory = new CommerceApiFactory(localDatabase.ConnectionString);
        using var localClient = localFactory.ShopperClient(); Basic(localClient);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(localClient, Encoding.UTF8.GetBytes("{}"))).StatusCode);
    }

    [PgFact]
    public async Task Authenticated_invalid_body_is_durable_invalid_input_and_oversized_body_is_rejected()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var factory = new BankCallbackApiFactory(database.ConnectionString);
        using var client = factory.ShopperClient(); Basic(client);
        var response = await SendAsync(client, Encoding.UTF8.GetBytes("{\"InstructionId\":\"known-invalid-input\"}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("0001", json.GetProperty("ResCode").GetString()); Assert.Equal("known-invalid-input", json.GetProperty("InstructionId").GetString());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await SendAsync(client, new byte[32769])).StatusCode);
        await using var db = database.Open(); Assert.Equal(1, await db.Observations.CountAsync()); Assert.Equal(0, await db.Allocations.CountAsync());
    }
}
