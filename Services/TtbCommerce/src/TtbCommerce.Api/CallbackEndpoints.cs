using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class CallbackEndpoints
{
    public static void AddCallbackTransport(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<TtbOptions>((forwarded, ttb) =>
        {
            forwarded.ForwardedHeaders = ttb.TrustedProxyCidrs.Length == 0 ? ForwardedHeaders.None : ForwardedHeaders.XForwardedFor;
            forwarded.ForwardLimit = 1;
            forwarded.KnownProxies.Clear(); forwarded.KnownIPNetworks.Clear();
            foreach (var cidr in ttb.TrustedProxyCidrs)
            {
                var network = System.Net.IPNetwork.Parse(cidr);
                if (network.PrefixLength == 0) throw new InvalidOperationException("A trusted proxy network cannot include every address.");
                forwarded.KnownIPNetworks.Add(network);
            }
        });
    }

    public static void MapCallbackEndpoints(this WebApplication app)
    {
        app.MapPost("/ttb/v1/credit-notification", async (HttpContext http, CommerceOptions commerce, TtbOptions ttb, PaymentProcessor payments, TimeProvider clock) =>
        {
            Authorize(http, commerce, ttb);
            if (http.Request.ContentLength > 32768) return Results.StatusCode(413);
            using var raw = new MemoryStream();
            var buffer = new byte[4096]; int read;
            while ((read = await http.Request.Body.ReadAsync(buffer, http.RequestAborted)) > 0)
            {
                if (raw.Length + read > 32768) return Results.StatusCode(413);
                raw.Write(buffer, 0, read);
            }
            using (raw)
            {
                var bytes = raw.ToArray();
                var service = http.Request.Headers["Service-Name"].ToString();
                var datetime = http.Request.Headers["Request-Datetime"].ToString();
                var metadata = new EvidenceMetadata(EvidenceSource.Callback, Guid.NewGuid().ToString("D"), datetime.Length <= 80 ? datetime : null,
                    service.Length <= 80 ? service : null, http.Connection.RemoteIpAddress?.ToString());
                var validHeaders = service == "credit-notification" && datetime.Length <= 40 && DateTimeOffset.TryParse(datetime, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
                var result = validHeaders ? await payments.NoticeAsync(bytes, metadata, http.RequestAborted) : await payments.InvalidAsync(bytes, metadata, http.RequestAborted);
                http.Response.Headers["Service-Name"] = "credit-notification";
                http.Response.Headers["Request-Datetime"] = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Numbering.Bangkok).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
                // This ACK is emitted only after the processor's durable transaction has committed.
                http.RequestServices.GetRequiredService<CallbackTelemetry>().CommittedAck(result.Ack.ResCode);
                return Results.Text(JsonSerializer.Serialize(result.Ack, BankWire.Json), "application/json; charset=utf-8", statusCode: 200);
            }
        });
    }

    private static void Authorize(HttpContext http, CommerceOptions commerce, TtbOptions ttb)
    {
        if (commerce.PaymentMode != PaymentMode.Bank || commerce.Environment == CommerceEnvironment.Local) throw new CommerceException(404, "not_found", "The callback is unavailable in this environment.");
        if (!ttb.CallbackProfileApproved || !ttb.CallbackSourceIpValidated || ttb.CallbackAuthentication != "Basic" || string.IsNullOrEmpty(ttb.CallbackUsername) ||
            string.IsNullOrEmpty(ttb.CallbackPassword) || ttb.MerchantId.Length != 3 || ttb.BillerNo.Length != 15)
            throw new CommerceException(503, "callback_unavailable", "The bank callback profile is not yet approved.");
        static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        var peer = http.Connection.RemoteIpAddress is { } address ? Normalize(address) : null;
        if (peer is null || !ttb.BankSourceIps.Any(x => IPAddress.TryParse(x, out var ip) && Normalize(ip).Equals(peer)))
            throw new CommerceException(403, "callback_source_rejected", "The callback source is not allowed.");
        var authorization = http.Request.Headers.Authorization.ToString();
        var authorized = false;
        if (authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) && authorization.Length <= 2048)
        {
            try
            {
                var credential = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(authorization[6..]));
                authorized = SecretTokens.Matches(credential, SecretTokens.Hash($"{ttb.CallbackUsername}:{ttb.CallbackPassword}"));
            }
            catch (Exception exception) when (exception is FormatException or DecoderFallbackException) { }
        }
        if (!authorized)
        {
            http.Response.Headers.WWWAuthenticate = "Basic realm=\"TTB Callback\"";
            throw new CommerceException(401, "callback_authentication_failed", "Callback authentication failed.");
        }
    }
}
