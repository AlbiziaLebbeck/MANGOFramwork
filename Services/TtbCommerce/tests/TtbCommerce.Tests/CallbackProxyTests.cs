using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TtbCommerce.Api;
using TtbCommerce.Core;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class CallbackProxyTests
{
    private const string BankIp = "203.148.143.101";
    private const string AttackerIp = "203.0.113.20";

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, byte[] raw, string? forwardedFor, string? realIp = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ttb/v1/credit-notification") { Content = new ByteArrayContent(raw) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("Service-Name", "credit-notification");
        request.Headers.Add("Request-Datetime", "2026-10-18T10:26:03.489+07:00");
        request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("unit-test-callback:unit-test-only-not-a-bank-secret")));
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        if (realIp is not null) request.Headers.Add("X-Real-IP", realIp);
        return await client.SendAsync(request);
    }

    [PgFact]
    public async Task A_trusted_single_proxy_uses_only_its_rightmost_peer_and_cannot_promote_a_forged_bank_header()
    {
        await using var database = await PgDatabase.CreateAsync();
        var fixture = await PaymentFixture.CreateAsync(database);
        await using var factory = new BankCallbackApiFactory(database.ConnectionString)
        {
            TrustedProxyCidrs = ["10.42.0.0/24"], Peer = IPAddress.Parse("10.42.0.20")
        };
        using var client = factory.ShopperClient();
        // A caller-supplied left entry cannot override the peer appended by a trusted proxy.
        using (var forged = await SendAsync(client, fixture.Raw, BankIp + ", " + AttackerIp, BankIp))
            Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        using (var missing = await SendAsync(client, fixture.Raw, null, BankIp))
            Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        using (var invalid = await SendAsync(client, fixture.Raw, "not-an-ip", BankIp))
            Assert.Equal(HttpStatusCode.Forbidden, invalid.StatusCode);
        await using (var check = database.Open())
        {
            Assert.Equal(0, await check.Observations.CountAsync());
            Assert.Equal(0, await check.Allocations.CountAsync());
            Assert.Equal(PaymentState.Unpaid, (await check.Orders.SingleAsync()).PaymentState);
        }
        // The actual bank peer is accepted through the same configured middleware, once.
        using var accepted = await SendAsync(client, fixture.Raw, AttackerIp + ", " + BankIp, AttackerIp);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("0000", (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ResCode").GetString());
        using var duplicate = await SendAsync(client, fixture.Raw, BankIp);
        Assert.Equal("0004", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ResCode").GetString());
        await using var committed = database.Open();
        Assert.Equal(1, await committed.BankTransactions.CountAsync());
        Assert.Equal(1, await committed.Allocations.CountAsync());
        Assert.Equal(PaymentState.Paid, (await committed.Orders.SingleAsync()).PaymentState);
        foreach (var observation in await committed.Observations.ToListAsync())
        {
            using var metadata = JsonDocument.Parse(observation.MetadataJson);
            Assert.Equal(BankIp, metadata.RootElement.GetProperty("peerIp").GetString());
        }
        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(1, options.ForwardLimit);
        Assert.Equal(ForwardedHeaders.XForwardedFor, options.ForwardedHeaders);
    }

    [PgFact]
    public async Task An_untrusted_proxy_cannot_forward_a_bank_identity_even_with_valid_callback_credentials()
    {
        await using var database = await PgDatabase.CreateAsync();
        var fixture = await PaymentFixture.CreateAsync(database);
        await using var factory = new BankCallbackApiFactory(database.ConnectionString)
        {
            TrustedProxyCidrs = ["10.42.0.0/24"], Peer = IPAddress.Parse("10.43.0.20")
        };
        using var client = factory.ShopperClient();
        using (var outsideNetwork = await SendAsync(client, fixture.Raw, BankIp, BankIp))
            Assert.Equal(HttpStatusCode.Forbidden, outsideNetwork.StatusCode);
        factory.Peer = IPAddress.Parse("::ffff:10.43.0.20");
        using (var mappedOutsideNetwork = await SendAsync(client, fixture.Raw, BankIp))
            Assert.Equal(HttpStatusCode.Forbidden, mappedOutsideNetwork.StatusCode);
        await using (var check = database.Open())
        {
            Assert.Equal(0, await check.Observations.CountAsync());
            Assert.Equal(0, await check.BankTransactions.CountAsync());
            Assert.Equal(0, await check.Allocations.CountAsync());
        }
        // A mapped IPv4 trusted proxy must still use the genuine rightmost bank peer.
        factory.Peer = IPAddress.Parse("::ffff:10.42.0.20");
        using var accepted = await SendAsync(client, fixture.Raw, BankIp);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("0000", (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ResCode").GetString());
        await using var committed = database.Open();
        Assert.Equal(1, await committed.Allocations.CountAsync());
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void Universal_proxy_trust_is_rejected_before_forwarded_headers_can_be_used(string network)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton(new TtbOptions { TrustedProxyCidrs = [network] });
        services.AddCallbackTransport();
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
    }
}
