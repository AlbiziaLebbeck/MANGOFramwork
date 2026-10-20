using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TtbCommerce.Api;
using TtbCommerce.Core;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class TransportProbeTests
{
    private const string Token = "SYNTHETIC-TRANSPORT-METRICS-TOKEN-ONLY";
    private const string Authorization = "Bearer " + Token;
    private const string Nonce = "d087b108598e44509dd58ded5c6b94ba";
    private const string ClientIp = "203.0.113.22";
    private static string Mac(string nonce, string ip) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Token),
        Encoding.UTF8.GetBytes("ttb-uat-transport-probe-v1\n" + nonce + "\n" + ip))).ToLowerInvariant();

    private sealed class Pipeline : IDisposable
    {
        public MutableClock Clock { get; } = new(DateTimeOffset.UtcNow);
        public CommerceOptions Commerce { get; } = new() { Environment = CommerceEnvironment.Uat, PaymentMode = PaymentMode.Bank };
        public OperationsOptions Operations { get; }
        public TtbOptions Bank { get; } = new() { TrustedProxyCidrs = ["10.42.0.7/32"] };
        public IPAddress? Peer { get; set; } = IPAddress.Parse("10.42.0.7");
        private readonly WebApplication application;
        public HttpClient Client { get; }
        public Pipeline(bool trust = true)
        {
            Operations = new() { MetricsEnabled = true, MetricsToken = Token, TransportProbeUntilUtc = Clock.GetUtcNow().AddMinutes(15) };
            if (!trust) Bank.TrustedProxyCidrs = [];
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(Commerce); builder.Services.AddSingleton(Operations); builder.Services.AddSingleton(Bank);
            builder.Services.AddSingleton<TimeProvider>(Clock); builder.Services.AddCallbackTransport();
            application = builder.Build();
            application.Use((context, next) => { context.Connection.RemoteIpAddress = Peer; return next(context); });
            application.UseUatTransportProbe(); application.UseForwardedHeaders();
            application.Run(http => http.Response.WriteAsJsonAsync(new { status = "alive", service = "ttb-commerce" }));
            application.StartAsync().GetAwaiter().GetResult(); Client = application.GetTestClient();
        }
        public void Dispose() { Client.Dispose(); application.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static Task<HttpResponseMessage> Request(HttpClient client, string path = "/health/live?transport_probe=" + Nonce,
        string? auth = Authorization, string? forwardedFor = ClientIp, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (auth is not null) request.Headers.TryAddWithoutValidation("Authorization", auth);
        if (forwardedFor is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        request.Headers.TryAddWithoutValidation("X-Real-IP", "203.148.143.101");
        request.Headers.TryAddWithoutValidation(UatTransportProbe.EffectiveHeader, "forged-response-digest");
        return client.SendAsync(request);
    }
    private static void NoProof(HttpResponseMessage response)
    {
        Assert.False(response.Headers.Contains(UatTransportProbe.SocketHeader));
        Assert.False(response.Headers.Contains(UatTransportProbe.EffectiveHeader));
    }

    [Theory]
    [InlineData("10.42.0.7")]
    [InlineData("::ffff:10.42.0.7")]
    public async Task Authorized_UAT_probe_observes_socket_and_only_the_rightmost_forwarded_peer_without_exporting_addresses(string socket)
    {
        using var fixture = new Pipeline { Peer = IPAddress.Parse(socket) };
        using var response = await Request(fixture.Client, forwardedFor: "203.148.143.101, " + ClientIp);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Mac(Nonce, "10.42.0.7"), Assert.Single(response.Headers.GetValues(UatTransportProbe.SocketHeader)));
        Assert.Equal(Mac(Nonce, ClientIp), Assert.Single(response.Headers.GetValues(UatTransportProbe.EffectiveHeader)));
        var content = await response.Content.ReadAsStringAsync(); Assert.Equal("{\"status\":\"alive\",\"service\":\"ttb-commerce\"}", content);
        var all = content + response.Headers; Assert.DoesNotContain(Token, all); Assert.DoesNotContain(ClientIp, all); Assert.DoesNotContain("10.42.0.7", all);
        const string anotherNonce = "9b34eb081cc4fb8b89002b449c6ef48e";
        using var another = await Request(fixture.Client, path: "/health/live?transport_probe=" + anotherNonce);
        Assert.Equal(Mac(anotherNonce, ClientIp), Assert.Single(another.Headers.GetValues(UatTransportProbe.EffectiveHeader)));
        Assert.NotEqual(Assert.Single(response.Headers.GetValues(UatTransportProbe.EffectiveHeader)), Assert.Single(another.Headers.GetValues(UatTransportProbe.EffectiveHeader)));
    }

    [Fact]
    public async Task Missing_wrong_or_oversized_credentials_and_invalid_or_duplicate_nonce_never_reveal_a_proof()
    {
        using var fixture = new Pipeline();
        foreach (var auth in new string?[] { null, "Bearer wrong", "bearer " + Token, "Basic " + Token, "Bearer " + new string('x', 1025) })
        { using var response = await Request(fixture.Client, auth: auth); NoProof(response); }
        foreach (var query in new[] { "", "?transport_probe=short", "?transport_probe=" + Nonce.ToUpperInvariant(), "?transport_probe=" + Nonce + "&transport_probe=" + Nonce })
        { using var response = await Request(fixture.Client, "/health/live" + query); NoProof(response); }
        fixture.Operations.MetricsEnabled = false;
        using (var disabled = await Request(fixture.Client)) NoProof(disabled);
        fixture.Operations.MetricsEnabled = true; fixture.Operations.MetricsToken = "short";
        using (var shortToken = await Request(fixture.Client, auth: "Bearer short")) NoProof(shortToken);
    }

    [Fact]
    public async Task The_probe_is_UAT_only_and_does_not_run_on_readiness_callback_or_non_GET_routes()
    {
        using var fixture = new Pipeline();
        foreach (var environment in new[] { CommerceEnvironment.Local, CommerceEnvironment.Production })
        { fixture.Commerce.Environment = environment; using var response = await Request(fixture.Client); NoProof(response); }
        fixture.Commerce.Environment = CommerceEnvironment.Uat;
        foreach (var path in new[] { "/health/ready", "/ttb/v1/credit-notification", "/internal/metrics", "/HEALTH/live" })
        { using var response = await Request(fixture.Client, path + "?transport_probe=" + Nonce); NoProof(response); }
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Head })
        { using var response = await Request(fixture.Client, method: method); NoProof(response); }
    }

    [Fact]
    public async Task Disabled_trust_or_an_untrusted_socket_cannot_turn_forged_headers_into_the_effective_identity()
    {
        using var fixture = new Pipeline(trust: false);
        using (var disabled = await Request(fixture.Client))
            Assert.Equal(Mac(Nonce, "10.42.0.7"), Assert.Single(disabled.Headers.GetValues(UatTransportProbe.EffectiveHeader)));
        // ForwardedHeaders options are built once. Use a second configured pipeline for untrusted transport.
        using var outside = new Pipeline { Peer = IPAddress.Parse("10.42.0.8") };
        using var untrusted = await Request(outside.Client);
        Assert.Equal(Mac(Nonce, "10.42.0.8"), Assert.Single(untrusted.Headers.GetValues(UatTransportProbe.SocketHeader)));
        Assert.Equal(Mac(Nonce, "10.42.0.8"), Assert.Single(untrusted.Headers.GetValues(UatTransportProbe.EffectiveHeader)));
    }

    [Fact]
    public async Task The_observation_window_expires_without_a_restart_and_a_far_future_window_is_disabled()
    {
        using var fixture = new Pipeline();
        using (var active = await Request(fixture.Client)) Assert.True(active.Headers.Contains(UatTransportProbe.EffectiveHeader));
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        using (var expired = await Request(fixture.Client)) NoProof(expired);
        fixture.Operations.TransportProbeUntilUtc = fixture.Clock.GetUtcNow().AddMinutes(31);
        using (var tooLong = await Request(fixture.Client)) NoProof(tooLong);
        fixture.Operations.TransportProbeUntilUtc = null;
        using (var absent = await Request(fixture.Client)) NoProof(absent);
    }

    [Fact]
    public async Task A_missing_socket_does_not_fabricate_transport_evidence()
    {
        using var fixture = new Pipeline { Peer = null };
        using var response = await Request(fixture.Client); NoProof(response);
    }

    private sealed class ApiFactory(string connection, DateTimeOffset until) : CommerceApiFactory(connection, environment: CommerceEnvironment.Uat)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Operations:MetricsEnabled"] = "true", ["Operations:MetricsToken"] = Token, ["Operations:TransportProbeUntilUtc"] = until.ToString("O"), ["Ttb:TrustedProxyCidrs:0"] = "10.42.0.7/32" }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new PeerFilter()));
        }
        private sealed class PeerFilter : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            { app.Use((http, continuation) => { http.Connection.RemoteIpAddress = IPAddress.Parse("10.42.0.7"); return continuation(http); }); next(app); };
        }
    }

    [PgFact]
    public async Task Real_API_health_observation_preserves_cache_policy_and_never_opens_bank_gates_or_writes_financial_evidence()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var factory = new ApiFactory(database.ConnectionString, DateTimeOffset.UtcNow.AddMinutes(15));
        using var client = factory.ShopperClient();
        using var response = await Request(client);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(Mac(Nonce, ClientIp), Assert.Single(response.Headers.GetValues(UatTransportProbe.EffectiveHeader)));
        using var ready = await client.GetAsync("/health/ready"); NoProof(ready);
        using var document = System.Text.Json.JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("paymentsReady").GetBoolean());
        using var callback = await Request(client, "/ttb/v1/credit-notification?transport_probe=" + Nonce, method: HttpMethod.Post);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, callback.StatusCode); NoProof(callback);
        await using var db = database.Open();
        Assert.Equal(0, await db.Orders.CountAsync()); Assert.Equal(0, await db.Observations.CountAsync());
        Assert.Equal(0, await db.BankTransactions.CountAsync()); Assert.Equal(0, await db.Allocations.CountAsync());
        Assert.Equal(0, await db.CallbackReceipts.CountAsync()); Assert.Equal(0, await db.Voids.CountAsync());
    }
}
