using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed record TrustedIdentity(string Issuer, string Subject, string DisplayName);
public sealed record ShopperActor(Guid ShopperId, string Issuer, string Subject, string DisplayName, string[] Roles)
{
    public string Key => $"{Issuer}:{Subject}";
    public bool HasRole(string role) => Roles.Contains("admin", StringComparer.Ordinal) || Roles.Contains(role, StringComparer.Ordinal);
}

// Credentials are deliberately not records: generated ToString() must never print their contents.
public sealed class IdentityCredentials
{
    public string? Authorization { get; init; }
    public string? Cookie { get; init; }
    public string Fingerprint => SecretTokens.Hash($"{Authorization}\n{Cookie}");
    public bool IsEmpty => string.IsNullOrEmpty(Authorization) && string.IsNullOrEmpty(Cookie);

    public static IdentityCredentials Extract(string authorization, string cookies, string[] allowedCookieNames)
    {
        if (authorization.Length > 16384 || cookies.Length > 16384 || cookies.Contains('\r') || cookies.Contains('\n')) throw new CommerceException(400, "credentials_too_large", "Authentication headers are invalid or too large.");
        var bearer = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && !authorization.Contains('\r') && !authorization.Contains('\n')
            ? authorization : null;
        var selected = cookies.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(x => (Raw: x, Name: x.Split('=', 2)[0]))
            .Where(x => x.Raw.Contains('=') && !x.Name.StartsWith("__Host-ttb-", StringComparison.Ordinal) && !x.Name.StartsWith("ttb-local-", StringComparison.Ordinal)
                && (allowedCookieNames.Contains("*", StringComparer.Ordinal) || allowedCookieNames.Contains(x.Name, StringComparer.Ordinal)))
            .OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => x.Raw).ToArray();
        return new() { Authorization = bearer, Cookie = bearer is null && selected.Length > 0 ? string.Join("; ", selected) : null };
    }
}

public static class SecretTokens
{
    public static string New() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool Matches(string value, string expectedHash) => expectedHash.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(value)), Encoding.ASCII.GetBytes(expectedHash));
}

public interface IIdentityVerifier
{
    Task<TrustedIdentity?> VerifyAsync(IdentityCredentials credentials, bool force, CancellationToken cancellationToken);
}

public sealed class MangosIdentityVerifier(HttpClient client, CommerceOptions options, IMemoryCache cache) : IIdentityVerifier
{
    public async Task<TrustedIdentity?> VerifyAsync(IdentityCredentials credentials, bool force, CancellationToken cancellationToken)
    {
        if (credentials.IsEmpty) return null;
        var key = $"mangos:{credentials.Fingerprint}";
        if (!force && cache.TryGetValue<TrustedIdentity>(key, out var cached)) return cached;
        using var request = new HttpRequestMessage(HttpMethod.Get, options.MangosUserUrl);
        if (credentials.Authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", credentials.Authorization);
        if (credentials.Cookie is not null) request.Headers.TryAddWithoutValidation("Cookie", credentials.Cookie);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { cache.Remove(key); return null; }
            if (!response.IsSuccessStatusCode) throw new CommerceException(503, "identity_unavailable", "The account service is temporarily unavailable.");
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var limited = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (limited.Length + read > 262144) throw new CommerceException(503, "identity_unavailable", "The account response exceeded its limit.");
                limited.Write(buffer, 0, read);
            }
            using var document = JsonDocument.Parse(limited.ToArray());
            var identity = Parse(document.RootElement);
            if (identity is not null) cache.Set(key, identity, TimeSpan.FromSeconds(30));
            return identity;
        }
        catch (HttpRequestException) { throw new CommerceException(503, "identity_unavailable", "The account service could not be reached."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new CommerceException(503, "identity_unavailable", "The account service timed out."); }
        catch (JsonException) { throw new CommerceException(503, "identity_unavailable", "The account service returned an invalid response."); }
    }

    public static TrustedIdentity? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("User", out var user) || user.ValueKind != JsonValueKind.Object ||
            !user.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()) || id.GetString()!.Length > 200)
            return null;
        static string Text(JsonElement obj, string name) => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var name = $"{Text(user, "firstName")} {Text(user, "lastName")}".Trim();
        return new("mangosgo", id.GetString()!, name.Length > 0 ? name[..Math.Min(name.Length, 200)] : id.GetString()!);
    }
}

public sealed record IssuedSession(string Token, string CsrfToken, DateTimeOffset ExpiresAt, ShopperActor Actor);
public sealed record ResolvedSession(ShopperSession Session, ShopperActor Actor);

public sealed class SessionService(CommerceDb db, CommerceOptions options, IIdentityVerifier identityVerifier, TimeProvider clock)
{
    public async Task<IssuedSession> ExchangeAsync(IdentityCredentials credentials, string? oldToken, CancellationToken cancellationToken)
    {
        var identity = await identityVerifier.VerifyAsync(credentials, true, cancellationToken)
            ?? throw new CommerceException(401, "login_required", "Please sign in with MANGOs.");
        return await IssueAsync(identity, credentials.Fingerprint, oldToken, cancellationToken);
    }

    public async Task<IssuedSession> LocalAsync(string subject, string? oldToken, CancellationToken cancellationToken)
    {
        if (options.Environment != CommerceEnvironment.Local || !options.LocalTestIdentityEnabled)
            throw new CommerceException(404, "not_found", "The resource was not found.");
        if (subject.Length is < 1 or > 100 || subject.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new CommerceException(400, "invalid_subject", "Use a simple local test account name.");
        return await IssueAsync(new("local", subject, subject), "local-development", oldToken, cancellationToken);
    }

    private async Task<IssuedSession> IssueAsync(TrustedIdentity identity, string fingerprint, string? oldToken, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var identityKey = $"{identity.Issuer}:{identity.Subject}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({identityKey}, 0))", cancellationToken);
        var shopper = await db.Shoppers.SingleOrDefaultAsync(x => x.Issuer == identity.Issuer && x.Subject == identity.Subject, cancellationToken);
        if (shopper is null) { shopper = new Shopper { Issuer = identity.Issuer, Subject = identity.Subject }; db.Shoppers.Add(shopper); }
        shopper.DisplayName = identity.DisplayName;
        if (!string.IsNullOrEmpty(oldToken))
        {
            var hash = SecretTokens.Hash(oldToken);
            await db.Sessions.Where(x => x.TokenHash == hash && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
        }
        var token = SecretTokens.New();
        var csrf = SecretTokens.New();
        var expires = now.AddHours(6);
        db.Sessions.Add(new ShopperSession { ShopperId = shopper.Id, TokenHash = SecretTokens.Hash(token), CsrfHash = SecretTokens.Hash(csrf), IdentityFingerprint = fingerprint, ExpiresAt = expires });
        db.AuditEvents.Add(new AuditEvent { Actor = identityKey, Action = "session.issue", Resource = $"shopper:{shopper.Id}", DataJson = "{}" });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(token, csrf, expires, Actor(shopper));
    }

    public async Task<ResolvedSession?> ResolveAsync(string? token, IdentityCredentials credentials, CancellationToken cancellationToken)
    {
        if (token is null || token.Length != 64) return null;
        var hash = SecretTokens.Hash(token);
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAt == null && x.ExpiresAt > clock.GetUtcNow(), cancellationToken);
        if (session is null) return null;
        var shopper = await db.Shoppers.AsNoTracking().SingleAsync(x => x.Id == session.ShopperId, cancellationToken);
        if (shopper.Issuer == "local")
        {
            if (options.Environment != CommerceEnvironment.Local || !options.LocalTestIdentityEnabled || session.IdentityFingerprint != "local-development") return null;
        }
        else
        {
            if (credentials.IsEmpty || session.IdentityFingerprint != credentials.Fingerprint) { await RevokeAsync(session.Id, cancellationToken); return null; }
            var identity = await identityVerifier.VerifyAsync(credentials, false, cancellationToken);
            if (identity?.Issuer != shopper.Issuer || identity.Subject != shopper.Subject) { await RevokeAsync(session.Id, cancellationToken); return null; }
        }
        return new(session, Actor(shopper));
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await db.Sessions.Where(x => x.Id == sessionId && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), cancellationToken);

    private ShopperActor Actor(Shopper shopper)
    {
        var key = $"{shopper.Issuer}:{shopper.Subject}";
        var roles = new List<string>();
        if (options.AdminSubjects.Contains(key, StringComparer.Ordinal)) roles.Add("admin");
        if (options.FinanceSubjects.Contains(key, StringComparer.Ordinal)) roles.Add("finance");
        if (options.FulfillmentSubjects.Contains(key, StringComparer.Ordinal)) roles.Add("fulfillment");
        return new(shopper.Id, shopper.Issuer, shopper.Subject, shopper.DisplayName, [.. roles]);
    }
}
