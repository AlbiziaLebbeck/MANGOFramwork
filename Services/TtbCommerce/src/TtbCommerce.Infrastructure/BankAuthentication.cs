using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class BankReadiness(CommerceOptions commerce, TtbOptions ttb, ReportOptions? reports = null)
{
    public IReadOnlyList<string> Missing()
    {
        if (commerce.PaymentMode == PaymentMode.Mock) return [];
        var missing = ttb.MissingBankRequirements().ToList();
        // SFTP/PGP secrets belong only to the commerce worker. API checkout uses the explicit report acceptance gate;
        // live worker health and the bank/file acceptance evidence are separate operational checks.
        if (commerce.Environment == CommerceEnvironment.Production && (reports is null || !reports.Enabled || !reports.AcceptanceApproved || reports.MissingParser().Count > 0))
            missing.Add("Reports.ProductionMandatoryProfile");
        if (commerce.Environment == CommerceEnvironment.Local) missing.Add("BankEnvironment");
        if (!BankSigningKeyProbe.Usable(ttb.PrivateKeyPath)) missing.Add("Ttb.SigningPrivateKey");
        return missing.Distinct(StringComparer.Ordinal).ToArray();
    }

    public void AssertReady()
    {
        if (Missing().Count > 0) throw new CommerceException(503, "bank_unavailable", "Bank payment setup is not yet complete. Please try again when payments are available.");
    }

    public void AssertOutgoing()
    {
        // Recovery/first acceptance queries remain possible before callback or gateway acceptance.
        var required = new[] { "Ttb.MerchantId", "Ttb.BillerNo", "Ttb.Issuer", "Ttb.PrivateKeyPath", "Ttb.TokenSeconds", "Ttb.SigningPrivateKey", "Ttb.SenderName", "BankEnvironment" };
        if (commerce.PaymentMode != PaymentMode.Bank || commerce.Environment == CommerceEnvironment.Local || Missing().Any(x => required.Contains(x, StringComparer.Ordinal)))
            throw new CommerceException(503, "bank_query_unavailable", "Bank enquiry authentication is not available.");
    }
}

public sealed class BankJwtSigner(TtbOptions options, TimeProvider clock)
{
    public string Create()
    {
        try
        {
            using var rsa = ReadKey(options.PrivateKeyPath);
            return Create(rsa, options.Issuer, clock.GetUtcNow(), options.TokenSeconds);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        { throw new CommerceException(503, "bank_signing_unavailable", "Bank authentication is temporarily unavailable."); }
    }

    internal static RSA ReadKey(string path)
    {
        var rsa = RSA.Create();
        try { rsa.ImportFromPem(File.ReadAllText(path)); return rsa; }
        catch { rsa.Dispose(); throw; }
    }

    public static string Create(RSA rsa, string issuer, DateTimeOffset now, int seconds)
    {
        if (rsa.KeySize != 2048 || string.IsNullOrWhiteSpace(issuer) || seconds is < 1 or > 1200) throw new ArgumentException("An approved RSA2048 issuer and short token lifetime are required.");
        // Literal field name from TTB's confirmed JWT profile (20 October 2026).
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", type = "JWT" }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new { iss = issuer, iat = now.ToUnixTimeSeconds(), exp = now.AddSeconds(seconds).ToUnixTimeSeconds() }));
        var signingInput = $"{header}.{payload}";
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Encode(signature)}";
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
