using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class CallbackBankSourceIpTests
{
    private const string BankIp = "203.148.143.101";
    private const string ProxyIp = "10.42.0.7";
    private const string AttackerIp = "203.0.113.20";

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, byte[] raw,
        string? forwarded = null, string service = "credit-notification", string datetime = "2026-10-21T11:02:00.123456+07:00")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ttb/v1/credit-notification") { Content = new ByteArrayContent(raw) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "UTF-8" };
        request.Headers.Add("Service-Name", service);
        request.Headers.Add("Request-Datetime", datetime);
        if (forwarded is not null) request.Headers.Add("X-Forwarded-For", forwarded);
        request.Headers.Add("X-Real-IP", BankIp); // Must never grant bank identity by itself.
        return await client.SendAsync(request);
    }

    [PgFact]
    public async Task Bank_common_headers_without_credentials_require_the_real_bank_peer_and_commit_once()
    {
        await using var database = await PgDatabase.CreateAsync();
        var fixture = await PaymentFixture.CreateAsync(database);
        await using var factory = new BankCallbackApiFactory(database.ConnectionString, sourceValidated: false,
            testCheckout: true, authentication: "BankSourceIp")
        { TrustedProxyCidrs = [ProxyIp + "/32"], Peer = IPAddress.Parse(AttackerIp) };
        using var client = factory.ShopperClient();
        var bank = factory.Services.GetRequiredService<TtbOptions>();
        Assert.Empty(bank.CallbackUsername); Assert.Empty(bank.CallbackPassword);
        Assert.DoesNotContain("Ttb.CallbackAuthenticationProfile", bank.MissingBankRequirements());

        // Direct callers and a forged leftmost entry cannot impersonate the bank.
        using (var direct = await SendAsync(client, fixture.Raw, BankIp)) Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
        factory.Peer = IPAddress.Parse(ProxyIp);
        using (var forged = await SendAsync(client, fixture.Raw, BankIp + ", " + AttackerIp)) Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        using (var missing = await SendAsync(client, fixture.Raw)) Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        factory.Peer = IPAddress.Parse("2001:db8::cb94:8f65");
        using (var ipv6 = await SendAsync(client, fixture.Raw, BankIp)) Assert.Equal(HttpStatusCode.Forbidden, ipv6.StatusCode);
        await using (var untouched = database.Open())
        {
            Assert.Equal(0, await untouched.Observations.CountAsync());
            Assert.Equal(PaymentState.Unpaid, (await untouched.Orders.SingleAsync()).PaymentState);
        }

        factory.Peer = IPAddress.Parse(ProxyIp);
        using var accepted = await SendAsync(client, fixture.Raw, AttackerIp + ", " + BankIp);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Empty(accepted.Headers.WwwAuthenticate);
        var ack = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("0000", ack.GetProperty("ResCode").GetString());
        Assert.Equal(fixture.Notice.InstructionId, ack.GetProperty("InstructionId").GetString());
        Assert.Equal("credit-notification", accepted.Headers.GetValues("Service-Name").Single());
        using var duplicate = await SendAsync(client, fixture.Raw, BankIp);
        Assert.Equal("0004", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ResCode").GetString());
        await using var committed = database.Open();
        Assert.Equal(1, await committed.BankTransactions.CountAsync());
        Assert.Equal(1, await committed.Allocations.CountAsync());
        Assert.Equal(2, (await committed.CallbackReceipts.SingleAsync()).DeliveryCount);
        Assert.Equal(PaymentState.Paid, (await committed.Orders.SingleAsync()).PaymentState);
        foreach (var observation in await committed.Observations.ToListAsync())
        {
            using var metadata = JsonDocument.Parse(observation.MetadataJson);
            Assert.Equal(BankIp, metadata.RootElement.GetProperty("peerIp").GetString());
        }
        // A synthetic development test must not claim real bank acceptance.
        Assert.False(bank.CallbackSourceIpValidated);
    }

    [PgFact]
    public async Task Source_only_profile_still_rejects_invalid_headers_and_mismatched_money_references_and_body_size()
    {
        await using var database = await PgDatabase.CreateAsync();
        var fixture = await PaymentFixture.CreateAsync(database);
        await using var factory = new BankCallbackApiFactory(database.ConnectionString, authentication: "BankSourceIp");
        using var client = factory.ShopperClient();
        using (var wrongService = await SendAsync(client, fixture.Raw, service: "wrong-service"))
            Assert.Equal("0001", (await wrongService.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ResCode").GetString());
        using (var wrongDate = await SendAsync(client, fixture.Raw, datetime: "invalid-date"))
            Assert.Equal("0001", (await wrongDate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ResCode").GetString());
        var cases = new[]
        {
            (fixture.Notice with { InstructionId = "TEST-AMOUNT", Amount = "1.00" }, "0005"),
            (fixture.Notice with { InstructionId = "TEST-REF1", Ref1 = "BADREF" }, "0006"),
            (fixture.Notice with { InstructionId = "TEST-BILLER", BillerNo = "222222222222222" }, "0003"),
            (fixture.Notice with { InstructionId = "TEST-QR", QrId = fixture.Notice.QrId![..15] + "99999" }, "0002")
        };
        foreach (var (notice, expected) in cases)
        {
            using var response = await SendAsync(client, JsonSerializer.SerializeToUtf8Bytes(notice, BankWire.Json));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expected, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ResCode").GetString());
        }
        using (var oversized = await SendAsync(client, new byte[32769])) Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        await using var db = database.Open();
        Assert.Equal(0, await db.Allocations.CountAsync());
        Assert.Equal(0, await db.Fulfillments.CountAsync());
        Assert.Equal(0, await db.Documents.CountAsync());
        Assert.Equal(PaymentState.Review, (await db.Orders.SingleAsync()).PaymentState);

        factory.Services.GetRequiredService<TtbOptions>().CallbackProfileApproved = false;
        using var closed = await SendAsync(client, fixture.Raw);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, closed.StatusCode);
    }

    [Fact]
    public void Source_profile_is_explicit_and_needs_approval_and_a_valid_exact_source_list()
    {
        var bank = new TtbOptions { CallbackAuthentication = "BankSourceIp", CallbackProfileApproved = true };
        Assert.True(bank.CallbackProfileReady());
        bank.CallbackProfileApproved = false; Assert.False(bank.CallbackProfileReady());
        bank.CallbackProfileApproved = true;
        bank.BankSourceIps = []; Assert.False(bank.CallbackProfileReady());
        bank.BankSourceIps = ["0.0.0.0/0"]; Assert.False(bank.CallbackProfileReady());
        bank.BankSourceIps = ["not-an-ip"]; Assert.False(bank.CallbackProfileReady());
        bank.BankSourceIps = [BankIp];
        bank.CallbackAuthentication = "None"; Assert.False(bank.CallbackProfileReady());
        bank.CallbackAuthentication = "Unavailable"; Assert.False(bank.CallbackProfileReady());
        bank.CallbackAuthentication = "Basic"; Assert.False(bank.CallbackProfileReady());
        bank.CallbackUsername = "unit"; bank.CallbackPassword = "unit"; Assert.True(bank.CallbackProfileReady());
    }
}
