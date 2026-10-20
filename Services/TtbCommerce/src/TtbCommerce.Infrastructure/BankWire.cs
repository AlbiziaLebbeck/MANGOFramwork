using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public static class BankWire
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false, MaxDepth = 16 };

    public static CreditEvidence ParseNotice(byte[] raw) => ParseCredit(raw, "000", optionalRef2: false);

    internal static CreditEvidence ParseCredit(byte[] raw, string expectedResultCode, bool optionalRef2)
    {
        using var document = JsonDocument.Parse(raw);
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().GroupBy(x => x.Name, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new FormatException("Expected a flat bank JSON object without duplicate keys.");
        var notice = JsonSerializer.Deserialize<CreditNotice>(raw, Json) ?? throw new FormatException("Missing notice.");
        if (!Text(notice.InstructionId, 50) || !Text(notice.BillerNo, 15) || !Text(notice.QrId, 20) || !Text(notice.Ref1, 20) || !(optionalRef2 ? Optional(notice.Ref2, 20) : Text(notice.Ref2, 20)) ||
            notice.ResultCode != expectedResultCode || !Text(notice.ResultDesc, 100) || !DateTime.TryParseExact(notice.TransDate, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            !Optional(notice.PayerAccount, 20) || !Optional(notice.PayerName, 40) || !Optional(notice.PayerBank, 4) || !Optional(notice.CrossBorderFlag, 1) ||
            !Optional(notice.SwitchCode, 6) || !Optional(notice.CurrencyCode, 3) || !Optional(notice.SwitchFullName, 100))
            throw new FormatException("Invalid credit notification fields.");
        var amount = Money.Parse(notice.Amount ?? "");
        if (amount.Minor == 0) throw new FormatException("Credit amount must be positive.");
        if (notice.CrossBorderFlag is not null and not "Y" || notice.CrossBorderFlag == "Y" &&
            (!Text(notice.SwitchCode, 6) || !Text(notice.CurrencyCode, 3) || !Text(notice.SwitchFullName, 100)))
            throw new FormatException("Incomplete cross-border metadata.");
        return new(notice.InstructionId!, notice.BillerNo!, notice.QrId!, notice.Ref1!, notice.Ref2!, amount.Minor, notice.ResultCode!, notice.TransDate!,
            notice.PayerAccount, notice.PayerName, notice.PayerBank, notice.CrossBorderFlag, notice.SwitchCode, notice.CurrencyCode, notice.SwitchFullName);
    }

    public static string Hash(byte[] raw) => Convert.ToHexStringLower(SHA256.HashData(raw));
    public static string BusinessHash(CreditEvidence evidence) => SecretTokens.Hash(JsonSerializer.Serialize(new
    {
        evidence.InstructionId, evidence.BillerNo, evidence.QrId, evidence.Ref1, evidence.Ref2, evidence.AmountMinor,
        evidence.CrossBorderFlag, evidence.SwitchCode, evidence.CurrencyCode
    }));

    public static string CanonicalHash(byte[] raw)
    {
        using var document = JsonDocument.Parse(raw);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, document.RootElement);
        return Hash(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool Optional(string? value, int maximum) => value is null || value.Length <= maximum && !value.Any(char.IsControl);
}
