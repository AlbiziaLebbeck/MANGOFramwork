using System.Globalization;
using System.Text.Json;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed record BankReadResult(string Outcome, CreditEvidence[] Credits, string? ResCode = null, string? Detail = null)
{
    public static BankReadResult Parse(BankOperation operation, BankReply reply, string expectedBiller, string expectedQr)
    {
        if (reply.TransportError is not null || reply.StatusCode is not >= 200 or >= 300) return new("Unconfirmed", [], Detail: reply.TransportError ?? "HttpRejected");
        try
        {
            using var document = JsonDocument.Parse(reply.Body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().GroupBy(x => x.Name, StringComparer.Ordinal).Any(x => x.Count() > 1)) return new("Unconfirmed", [], Detail: "MalformedResponse");
            if (Text(root, "BillerNo") != expectedBiller || Text(root, "QRId") != expectedQr) return new("Unconfirmed", [], Detail: "ResponseIdentityMismatch");
            var code = Text(root, "ResCode");
            if (!DateTime.TryParseExact(Text(root, "TransDate"), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return new("Unconfirmed", [], code, "MalformedResponse");
            if (operation == BankOperation.Verify)
            {
                if (code == "0002") return new("NotFound", [], code);
                if (code == "9001") return new("AlreadyVoided", [], code);
                if (code != "0000" || Text(root, "ResultCode") != "0000") return new("Unconfirmed", [], code, "UnsupportedBankResult");
                return new("Found", [ReadCredit(root, verify: true)], code);
            }
            if (operation != BankOperation.EnquiryRange) throw new ArgumentOutOfRangeException(nameof(operation));
            if (code == "0001") return new("NotFound", [], code);
            if (code != "0000" || !root.TryGetProperty("QTransaction", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() is < 1 or > 2000)
                return new("Unconfirmed", [], code, "UnsupportedBankResult");
            var credits = rows.EnumerateArray().Select(x => ReadCredit(x, verify: false)).ToArray();
            if (credits.Any(x => x.BillerNo != expectedBiller || x.QrId != expectedQr)) return new("Unconfirmed", [], code, "ResponseIdentityMismatch");
            return new("Found", credits, code);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        { return new("Unconfirmed", [], Detail: "MalformedResponse"); }
    }

    private static CreditEvidence ReadCredit(JsonElement root, bool verify)
    {
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().GroupBy(x => x.Name, StringComparer.Ordinal).Any(x => x.Count() > 1)) throw new FormatException("Invalid bank transaction object.");
        // Ref2 is optional only in Verify. Preserve its absence rather than inventing a bank-returned value.
        return BankWire.ParseCredit(System.Text.Encoding.UTF8.GetBytes(root.GetRawText()), verify ? "0000" : "000", optionalRef2: verify);
    }

    private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
