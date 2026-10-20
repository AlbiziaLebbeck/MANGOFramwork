using System.Net;
using System.Security.Cryptography;
using System.Text;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class UatTransportProbe
{
    public const string SocketHeader = "X-Ttb-Transport-Peer-Digest";
    public const string EffectiveHeader = "X-Ttb-Effective-Peer-Digest";

    // Register before ForwardedHeaders. The response callback runs after it has processed the request.
    public static IApplicationBuilder UseUatTransportProbe(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (!HttpMethods.IsGet(http.Request.Method) || !http.Request.Path.Equals("/health/live", StringComparison.Ordinal))
        { await next(http); return; }
        var commerce = http.RequestServices.GetRequiredService<CommerceOptions>();
        var options = http.RequestServices.GetRequiredService<OperationsOptions>();
        var now = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var nonce = http.Request.Query["transport_probe"];
        var authorization = http.Request.Headers.Authorization.ToString();
        if (commerce.Environment != CommerceEnvironment.Uat || !options.MetricsEnabled || options.MetricsToken.Length is < 32 or > 512 ||
            options.TransportProbeUntilUtc is not { } until || until <= now || until > now.AddMinutes(30) ||
            nonce.Count != 1 || nonce[0] is not { Length: 32 } value || value.Any(c => !char.IsAsciiHexDigit(c) || char.IsAsciiLetterUpper(c)) ||
            authorization.Length > 1024 || !authorization.StartsWith("Bearer ", StringComparison.Ordinal) ||
            !SecretTokens.Matches(authorization[7..], SecretTokens.Hash(options.MetricsToken)))
        { await next(http); return; }
        var socketPeer = http.Connection.RemoteIpAddress;
        http.Response.OnStarting(() =>
        {
            var effectivePeer = http.Connection.RemoteIpAddress;
            // Missing transport metadata is not evidence of a known peer.
            if (socketPeer is not null && effectivePeer is not null)
            {
                string Digest(IPAddress address)
                {
                    if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
                    var input = Encoding.UTF8.GetBytes("ttb-uat-transport-probe-v1\n" + value + "\n" + address);
                    return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.MetricsToken), input)).ToLowerInvariant();
                }
                http.Response.Headers[SocketHeader] = Digest(socketPeer);
                http.Response.Headers[EffectiveHeader] = Digest(effectivePeer);
            }
            return Task.CompletedTask;
        });
        await next(http);
    });
}
