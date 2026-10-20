using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public class CommerceApiFactory(string connectionString, bool enableLocal = true, CommerceEnvironment environment = CommerceEnvironment.Local, bool enableWorker = false) : WebApplicationFactory<Program>
{
    public TestIdentityVerifier Identity { get; } = new();
    public string? PrivateRoot { get; set; }
    public string GameKey { get; set; } = "";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Commerce"] = connectionString,
            ["Storage:RootPath"] = PrivateRoot ?? "", ["Storage:GameServerKey"] = GameKey,
            ["Commerce:Environment"] = environment.ToString(), ["Commerce:PaymentMode"] = environment == CommerceEnvironment.Local ? "Mock" : "Bank",
            ["Commerce:Role"] = enableWorker ? "CommerceWorker" : "Api",
            ["Commerce:LocalTestIdentityEnabled"] = (enableLocal && environment == CommerceEnvironment.Local).ToString(),
            ["Commerce:TrustedOrigins:0"] = "https://shop.test",
            ["Commerce:MangosCookieNames:0"] = "mangos_sso",
            ["Commerce:AdminSubjects:0"] = "mangosgo:admin",
            ["Logging:LogLevel:Default"] = "Warning"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IIdentityVerifier>();
            services.AddSingleton<IIdentityVerifier>(Identity);
        });
    }

    public HttpClient ShopperClient(string? subject = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Origin", "https://shop.test");
        if (subject is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", subject);
        return client;
    }
}

public sealed class TestIdentityVerifier : IIdentityVerifier
{
    public HashSet<string> Revoked { get; } = [];
    public Task<TrustedIdentity?> VerifyAsync(IdentityCredentials credentials, bool force, CancellationToken cancellationToken)
    {
        var subject = credentials.Authorization?.Replace("Bearer ", "", StringComparison.Ordinal);
        return Task.FromResult(subject is null || Revoked.Contains(subject) ? null : new TrustedIdentity("mangosgo", subject, $"User {subject}"));
    }
}

public sealed class IdentityTests
{
    private const string Prefix = "/ttb-commerce/v1";

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"forged\"}")]
    [InlineData("{\"data\":{\"User\":{\"id\":42}}}")]
    [InlineData("{\"data\":{\"User\":{\"id\":\"\"}}}")]
    public void Identity_parser_never_trusts_a_client_shape_or_missing_subject(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Null(MangosIdentityVerifier.Parse(document.RootElement));
    }

    [Fact]
    public void Identity_parser_uses_the_existing_Mangos_response_contract()
    {
        using var document = JsonDocument.Parse("""{"data":{"User":{"id":"42","firstName":"สมชาย","lastName":"ใจดี","roles":["admin"]}}}""");
        var identity = Assert.IsType<TrustedIdentity>(MangosIdentityVerifier.Parse(document.RootElement));
        Assert.Equal("mangosgo", identity.Issuer);
        Assert.Equal("42", identity.Subject);
        Assert.Equal("สมชาย ใจดี", identity.DisplayName);
    }

    [Fact]
    public void Cookie_forwarding_omits_commerce_tokens_and_is_allowlisted()
    {
        var credentials = IdentityCredentials.Extract("", "ttb-local-session=private; mangos_sso=trusted; unrelated=no", ["mangos_sso"]);
        Assert.Equal("mangos_sso=trusted", credentials.Cookie);
        Assert.Null(IdentityCredentials.Extract("", "mangos_sso=trusted", []).Cookie);
        Assert.Equal("mangos_sso=trusted; unrelated=no", IdentityCredentials.Extract("", "__Host-ttb-session-uat=private; mangos_sso=trusted; unrelated=no", ["*"]).Cookie);
        Assert.Null(IdentityCredentials.Extract("Bearer token", "mangos_sso=cookie", ["*"]).Cookie);
        Assert.DoesNotContain("trusted", credentials.ToString()!);
    }

    [PgFact]
    public async Task Bootstrap_uses_trusted_subject_and_stores_only_hashes()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var factory = new CommerceApiFactory(database.ConnectionString);
        using var client = factory.ShopperClient("alice");
        var response = await client.PostAsJsonAsync($"{Prefix}/session", new { userId = "admin", roles = new[] { "admin" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(json.GetProperty("user").GetProperty("roles").EnumerateArray());
        var csrf = json.GetProperty("csrfToken").GetString()!;
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("ttb-local-session=", StringComparison.Ordinal));
        var token = cookie.Split(';')[0].Split('=')[1];
        await using var db = database.Open();
        var shopper = Assert.Single(db.Shoppers);
        Assert.Equal("alice", shopper.Subject);
        var session = Assert.Single(db.Sessions);
        Assert.NotEqual(token, session.TokenHash);
        Assert.NotEqual(csrf, session.CsrfHash);
        Assert.True(SecretTokens.Matches(token, session.TokenHash));
        Assert.True(SecretTokens.Matches(csrf, session.CsrfHash));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [PgFact]
    public async Task Account_switch_and_global_logout_cannot_reuse_previous_commerce_identity()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var factory = new CommerceApiFactory(database.ConnectionString);
        using var client = factory.ShopperClient("alice");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{Prefix}/session", null)).StatusCode);
        var alice = await client.GetFromJsonAsync<JsonElement>($"{Prefix}/session");
        var aliceId = alice.GetProperty("user").GetProperty("id").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bob");
        var switched = await client.GetFromJsonAsync<JsonElement>($"{Prefix}/session");
        Assert.False(switched.GetProperty("authenticated").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{Prefix}/session", null)).StatusCode);
        var bob = await client.GetFromJsonAsync<JsonElement>($"{Prefix}/session");
        Assert.NotEqual(aliceId, bob.GetProperty("user").GetProperty("id").GetString());
        factory.Identity.Revoked.Add("bob");
        Assert.False((await client.GetFromJsonAsync<JsonElement>($"{Prefix}/session")).GetProperty("authenticated").GetBoolean());
    }

    [PgFact]
    public async Task Mutations_reject_missing_csrf_and_untrusted_origin()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var factory = new CommerceApiFactory(database.ConnectionString);
        using var client = factory.ShopperClient("alice");
        var bootstrap = await client.PostAsync($"{Prefix}/session", null);
        var csrf = (await bootstrap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{Prefix}/session/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{Prefix}/session/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{Prefix}/session/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "https://shop.test");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{Prefix}/session/logout", null)).StatusCode);
        Assert.False((await client.GetFromJsonAsync<JsonElement>($"{Prefix}/session")).GetProperty("authenticated").GetBoolean());
    }

    [PgFact]
    public async Task Local_identity_endpoint_is_opt_in_and_cors_excludes_other_sites()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var factory = new CommerceApiFactory(database.ConnectionString, enableLocal: false);
        using var client = factory.ShopperClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{Prefix}/session/local", new { subject = "alice" })).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.test");
        var response = await client.GetAsync($"{Prefix}/session");
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [PgFact]
    public async Task Uat_can_serve_accounts_with_secure_cookies_while_bank_is_unavailable()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var factory = new CommerceApiFactory(database.ConnectionString, environment: CommerceEnvironment.Uat);
        using var client = factory.ShopperClient("alice");
        var ready = await client.GetFromJsonAsync<JsonElement>("/health/ready");
        Assert.True(ready.GetProperty("database").GetBoolean());
        Assert.False(ready.GetProperty("paymentsReady").GetBoolean());
        Assert.Equal("Bank", ready.GetProperty("paymentMode").GetString());
        var bootstrap = await client.PostAsync($"{Prefix}/session", null);
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        var cookie = bootstrap.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-ttb-session-uat=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{Prefix}/session/local", new { subject = "forged" })).StatusCode);
    }
}
