using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public enum BankOperation { Verify, EnquiryRange, MerchantVoid, EnquiryVoid }
public sealed record BankReply(int? StatusCode, byte[] Body, string? TransportError = null);
public interface IBankGateway
{
    void EnsureAvailable();
    void EnsureAvailable(BankOperation operation) => EnsureAvailable();
    Task<BankReply> PostAsync(BankOperation operation, object body, Guid requestUid, CancellationToken cancellationToken);
}

public sealed class TtbBankGateway(HttpClient client, CommerceOptions commerce, TtbOptions ttb, BankReadiness readiness, BankJwtSigner signer, TimeProvider clock, BankCircuitBreaker circuit) : IBankGateway
{
    public void EnsureAvailable() => readiness.AssertOutgoing();
    public void EnsureAvailable(BankOperation operation) { EnsureAvailable(); circuit.AssertAvailable(operation); }
    public async Task<BankReply> PostAsync(BankOperation operation, object body, Guid requestUid, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var (path, service) = operation switch
        {
            BankOperation.Verify => ("/qrapiservices/verify/verifyqr/", "verify-qr"),
            BankOperation.EnquiryRange => ("/qrapiservices/enquiry/enquiryqr-range/", "enquiry-qr-range"),
            BankOperation.MerchantVoid => ("/qrapiservices/void/merchantvoid/", "merchant-void"),
            BankOperation.EnquiryVoid => ("/qrapiservices/void/enquiryvoid/", "enquiry-void"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(TtbOptions.Gateway(commerce.Environment), path + ttb.MerchantId));
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, BankWire.Json));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Headers.Add("Service-Name", service); request.Headers.Add("Request-uid", requestUid.ToString("D"));
        request.Headers.Add("Request-Datetime", TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Numbering.Bangkok).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(ttb.SenderName)) request.Headers.Add("sender_name", ttb.SenderName);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signer.Create());
        var permit = circuit.TryEnter(operation);
        if (permit is null) return new(null, [], "CircuitOpen");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(client.Timeout == Timeout.InfiniteTimeSpan ? TimeSpan.FromSeconds(10) : client.Timeout);
        BankReply Finish(BankReply reply)
        {
            circuit.Complete(permit, reply.TransportError is not null || reply.StatusCode is null or >= 500 or 401 or 403 or 429);
            return reply;
        }
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (output.Length + read > 1048576) return Finish(new((int)response.StatusCode, output.ToArray(), "ResponseTooLarge"));
                output.Write(buffer, 0, read);
            }
            return Finish(new((int)response.StatusCode, output.ToArray()));
        }
        catch (HttpRequestException) { return Finish(new(null, [], "Unreachable")); }
        catch (IOException) { return Finish(new(null, [], "Unreachable")); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Finish(new(null, [], "Timeout")); }
        finally { circuit.Abandon(permit); }
    }

    public static string BankTime(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Numbering.Bangkok).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
    public static void ValidateRange(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        if (start > end || end > now || start < now.AddHours(-24))
            throw new CommerceException(400, "range_invalid", "Bank enquiry must stay within the previous24hours and cannot include future time.");
    }
}
