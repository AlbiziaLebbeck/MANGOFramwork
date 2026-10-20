using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class GatewayTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public int Count { get; private set; }
        public Uri? Uri { get; private set; }
        public Dictionary<string, string> Headers { get; private set; } = [];
        public JsonElement Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++; Uri = request.RequestUri; Headers = request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("utf-8", request.Content!.Headers.ContentType!.CharSet);
            Body = JsonSerializer.Deserialize<JsonElement>(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
        }
    }

    [Fact]
    public async Task Gateway_pins_TLS_host_and_path_signs_headers_and_does_not_retry_HTTP_failure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ttb-unit-key-{Guid.NewGuid():N}.pem");
        using var rsa = RSA.Create(2048);
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) await file.WriteAsync(Encoding.ASCII.GetBytes(rsa.ExportPkcs8PrivateKeyPem()));
        try
        {
            var commerce = PaymentFixture.Commerce;
            var options = PaymentFixture.Bank; options.PrivateKeyPath = path; options.Issuer = "unit-issuer"; options.SenderName = "UNIT";
            var now = new DateTimeOffset(2026, 10, 18, 1, 0, 0, TimeSpan.Zero); var clock = new MutableClock(now);
            using var handler = new CaptureHandler(); using var client = new HttpClient(handler);
            var gateway = new TtbBankGateway(client, commerce, options, new(commerce, options), new(options, clock), clock, new(new(), clock));
            // Outgoing recovery auth does not depend on callback availability.
            Assert.False(options.CallbackProfileApproved);
            // A first Verify must work while its acceptance gate is still false (avoid a bootstrap deadlock).
            Assert.False(options.GatewayAuthenticationValidated);
            Assert.Contains("Ttb.GatewayAuthenticationValidation", new BankReadiness(commerce, options).Missing());
            var uid = Guid.NewGuid();
            var reply = await gateway.PostAsync(BankOperation.Verify, new { BillerNo = options.BillerNo, QRId = BankReadTests.Qr, TransDate = TtbBankGateway.BankTime(now) }, uid, default);
            Assert.Equal(503, reply.StatusCode); Assert.Equal(1, handler.Count);
            Assert.Equal("https://uat-dgw.tau2904.com:9643/qrapiservices/verify/verifyqr/TST", handler.Uri!.AbsoluteUri);
            Assert.Equal("verify-qr", handler.Headers["Service-Name"]); Assert.Equal(uid.ToString("D"), handler.Headers["Request-uid"]); Assert.Equal("UNIT", handler.Headers["sender_name"]);
            Assert.Equal("2026-10-18T08:00:00.000+07:00", handler.Headers["Request-Datetime"]); Assert.Equal("20261018080000", handler.Body.GetProperty("TransDate").GetString());
            var parts = handler.Headers["Authorization"][7..].Split('.');
            var signature = Convert.FromBase64String(parts[2].Replace('-', '+').Replace('_', '/').PadRight((parts[2].Length + 3) / 4 * 4, '='));
            Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        finally { File.Delete(path); }
    }
}
