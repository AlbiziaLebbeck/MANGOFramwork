using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class BankKeyLifecycleTests
{
    private const string CollectorToken = "SYNTHETIC-KEY-LIFECYCLE-COLLECTOR-TOKEN";
    private sealed class Capture : HttpMessageHandler
    {
        public List<string> Tokens { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("uat-dgw.tau2904.com", request.RequestUri!.Host);
            Tokens.Add(request.Headers.Authorization!.Parameter!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
    private static void ReplaceKey(string path, string pem)
    {
        // Atomic projected-file replacement, with the same long-lived signer/gateway instance.
        var next = path + ".next"; File.WriteAllText(next, pem, new UTF8Encoding(false)); File.Move(next, path, overwrite: true);
    }
    private static bool Verifies(string token, RSA key)
    {
        var parts = token.Split('.'); var signature = parts[2].Replace('-', '+').Replace('_', '/');
        return key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Convert.FromBase64String(signature.PadRight((signature.Length + 3) / 4 * 4, '=')),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    [Fact]
    public async Task Mounted_JWT_key_rotation_changes_the_next_gateway_signature_without_a_cached_key_or_automatic_retry()
    {
        using var storage = new TestPrivateStorage(); Directory.CreateDirectory(storage.Root);
        var path = Path.Combine(storage.Root, "synthetic-bank.pem"); using var oldKey = RSA.Create(2048); using var newKey = RSA.Create(2048);
        ReplaceKey(path, oldKey.ExportPkcs8PrivateKeyPem());
        var bank = PaymentFixture.Bank; bank.PrivateKeyPath = path; bank.Issuer = "synthetic-rotation-issuer";
        var clock = new MutableClock(DateTimeOffset.Parse("2026-10-19T00:00:00Z"));
        using var handler = new Capture(); using var client = new HttpClient(handler);
        var gateway = new TtbBankGateway(client, PaymentFixture.Commerce, bank, new(PaymentFixture.Commerce, bank), new(bank, clock), clock, new(new(), clock));
        await gateway.PostAsync(BankOperation.Verify, new { Synthetic = true }, Guid.NewGuid(), default);
        Assert.Single(handler.Tokens); Assert.True(Verifies(handler.Tokens[0], oldKey)); Assert.False(Verifies(handler.Tokens[0], newKey));
        ReplaceKey(path, newKey.ExportPkcs8PrivateKeyPem()); clock.Advance(TimeSpan.FromSeconds(1));
        await gateway.PostAsync(BankOperation.EnquiryRange, new { Synthetic = true }, Guid.NewGuid(), default);
        Assert.Equal(2, handler.Tokens.Count); Assert.True(Verifies(handler.Tokens[1], newKey)); Assert.False(Verifies(handler.Tokens[1], oldKey));
        Assert.True(Verifies(handler.Tokens[0], oldKey));
        static JsonDocument Claims(string token)
        {
            var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            return JsonDocument.Parse(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
        }
        using var oldClaims = Claims(handler.Tokens[0]); using var newClaims = Claims(handler.Tokens[1]);
        Assert.Equal(bank.Issuer, newClaims.RootElement.GetProperty("iss").GetString());
        Assert.Equal(oldClaims.RootElement.GetProperty("iat").GetInt64() + 1, newClaims.RootElement.GetProperty("iat").GetInt64());
        foreach (var claims in new[] { oldClaims, newClaims })
            Assert.Equal(bank.TokenSeconds, claims.RootElement.GetProperty("exp").GetInt64() - claims.RootElement.GetProperty("iat").GetInt64());
        ReplaceKey(path, "invalid synthetic key material");
        var error = await Assert.ThrowsAsync<CommerceException>(() => gateway.PostAsync(BankOperation.Verify, new { Synthetic = true }, Guid.NewGuid(), default));
        Assert.Equal("bank_query_unavailable", error.Code); Assert.Equal(2, handler.Tokens.Count); Assert.DoesNotContain(path, error.Message);
        ReplaceKey(path, newKey.ExportPkcs8PrivateKeyPem());
        await gateway.PostAsync(BankOperation.Verify, new { Synthetic = true }, Guid.NewGuid(), default);
        Assert.Equal(3, handler.Tokens.Count); Assert.True(Verifies(handler.Tokens[2], newKey));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("public-only")]
    [InlineData("wrong-size")]
    public void Missing_public_only_or_wrong_size_keys_never_reuse_a_previous_signing_key_or_expose_key_details(string kind)
    {
        using var storage = new TestPrivateStorage(); Directory.CreateDirectory(storage.Root); var path = Path.Combine(storage.Root, "fixture.pem");
        using var rsa = RSA.Create(2048); var bank = PaymentFixture.Bank; bank.PrivateKeyPath = path; bank.Issuer = "synthetic-issuer";
        ReplaceKey(path, rsa.ExportPkcs8PrivateKeyPem()); var signer = new BankJwtSigner(bank, TimeProvider.System); Assert.True(Verifies(signer.Create(), rsa));
        if (kind == "missing") File.Delete(path);
        else if (kind == "public-only") ReplaceKey(path, rsa.ExportSubjectPublicKeyInfoPem());
        else { using var wrong = RSA.Create(1024); ReplaceKey(path, wrong.ExportPkcs8PrivateKeyPem()); }
        var error = Assert.Throws<CommerceException>(() => signer.Create()); Assert.Equal("bank_signing_unavailable", error.Code);
        Assert.DoesNotContain(path, error.Message); Assert.DoesNotContain("PRIVATE KEY", error.Message);
        Assert.Contains("Ttb.SigningPrivateKey", new BankReadiness(PaymentFixture.Commerce, bank).Missing());
        Assert.False(BankSigningKeyProbe.Read(PaymentFixture.Commerce, bank, new()).Usable);
    }

    [Fact]
    public void Bare_RSA_probe_has_no_invented_expiry_and_scheduling_requires_an_explicit_operational_deadline()
    {
        using var storage = new TestPrivateStorage(); Directory.CreateDirectory(storage.Root); using var key = RSA.Create(2048);
        var bank = PaymentFixture.Bank; bank.Issuer = "synthetic-issuer"; bank.PrivateKeyPath = Path.Combine(storage.Root, "key.pem"); ReplaceKey(bank.PrivateKeyPath, key.ExportPkcs8PrivateKeyPem());
        var status = BankSigningKeyProbe.Read(PaymentFixture.Commerce, bank, new());
        Assert.True(status.Expected); Assert.True(status.Usable); Assert.False(status.RotationScheduled); Assert.Null(status.RotationDueUnixSeconds);
        var due = DateTimeOffset.Parse("2026-12-01T00:00:00+07:00");
        status = BankSigningKeyProbe.Read(PaymentFixture.Commerce, bank, new() { BankSigningKeyRotationDueUtc = due });
        Assert.True(status.RotationScheduled); Assert.Equal(due.ToUnixTimeSeconds(), status.RotationDueUnixSeconds);
        bank.Issuer = ""; status = BankSigningKeyProbe.Read(PaymentFixture.Commerce, bank, new() { BankSigningKeyRotationDueUtc = due });
        Assert.False(status.Expected); Assert.False(status.RotationScheduled); Assert.Null(status.RotationDueUnixSeconds);
        bank.Issuer = "synthetic-issuer";
        status = BankSigningKeyProbe.Read(new() { Environment = CommerceEnvironment.Local, PaymentMode = PaymentMode.Mock }, bank, new() { BankSigningKeyRotationDueUtc = due });
        Assert.False(status.Expected); Assert.False(status.Usable); Assert.Null(status.RotationDueUnixSeconds);
    }

    private sealed class Factory(string connection, string path, DateTimeOffset due) : CommerceApiFactory(connection, false, CommerceEnvironment.Uat)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Operations:MetricsEnabled"] = "true", ["Operations:MetricsToken"] = CollectorToken,
                ["Operations:BankSigningKeyRotationDueUtc"] = due.ToString("O"), ["Ttb:Issuer"] = "synthetic-private-issuer",
                ["Ttb:PrivateKeyPath"] = path
            }));
        }
    }

    [PgFact]
    public async Task Private_key_metrics_survive_database_loss_and_report_missing_key_without_exporting_paths_or_material()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); Directory.CreateDirectory(storage.Root);
        using var key = RSA.Create(2048); var path = Path.Combine(storage.Root, "private-fixture.pem"); ReplaceKey(path, key.ExportPkcs8PrivateKeyPem());
        var due = DateTimeOffset.Parse("2026-12-01T00:00:00Z");
        await using var factory = new Factory(database.ConnectionString, path, due); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CollectorToken);
        var metrics = await client.GetStringAsync("/internal/metrics");
        Assert.Contains("ttb_bank_signing_key_expected 1\n", metrics); Assert.Contains("ttb_bank_signing_key_probe_success 1\n", metrics);
        Assert.Contains("ttb_bank_signing_key_rotation_scheduled 1\n", metrics);
        Assert.Contains($"ttb_bank_signing_key_rotation_due_timestamp_seconds {due.ToUnixTimeSeconds()}\n", metrics);
        await database.DisposeAsync(); File.Delete(path);
        metrics = await client.GetStringAsync("/internal/metrics");
        Assert.Contains("ttb_database_up 0\n", metrics); Assert.Contains("ttb_bank_signing_key_probe_success 0\n", metrics);
        Assert.DoesNotContain("ttb_jobs_pending", metrics); Assert.DoesNotContain(path, metrics); Assert.DoesNotContain("synthetic-private-issuer", metrics);
        Assert.DoesNotContain("PRIVATE KEY", metrics); Assert.DoesNotContain(CollectorToken, metrics);
        Assert.DoesNotContain("ttb_bank_signing_key_not_after", metrics);
    }
}
