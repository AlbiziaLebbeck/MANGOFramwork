using System.Globalization;
using System.Text.Json;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public static class VoidWindow
{
    public static VoidEligibility Check(BankTransaction bank, PaymentAttempt? attempt, VoidOptions options, DateTimeOffset now)
    {
        if (!options.ValidPolicy) return new(false, "void_policy_unapproved");
        if (bank.CrossBorderFlag == "Y") return new(false, "void_cross_border_unavailable");
        if (bank.InstructionId.StartsWith('Q')) return new(false, "void_instruction_ineligible");
        if (bank.Voided) return new(false, "already_voided");
        var proven = bank.PaymentOccurredAt is not null && bank.PaymentTimeSource == "ReportIsoPayment";
        if (!proven && attempt is null) return new(false, "void_original_time_unproven");
        var from = proven ? bank.PaymentOccurredAt!.Value : attempt!.CreatedAt;
        var until = proven ? bank.PaymentOccurredAt!.Value : bank.CreatedAt;
        if (from > until || until > now || bank.CreatedAt > now || attempt is not null && from < attempt.CreatedAt || until > bank.CreatedAt) return new(false, "void_original_time_unproven");
        DateOnly Day(DateTimeOffset value)
        {
            var local = TimeZoneInfo.ConvertTime(value, Numbering.Bangkok); var date = DateOnly.FromDateTime(local.DateTime);
            return options.WindowPolicy == VoidWindowPolicy.BankingDay2256 && TimeOnly.FromDateTime(local.DateTime) >= new TimeOnly(22, 56) ? date.AddDays(1) : date;
        }
        var day = Day(now); var end = new DateTimeOffset(day.ToDateTime(new TimeOnly(22, 56)), TimeSpan.FromHours(7)).ToUniversalTime();
        if (Day(from) != day || Day(until) != day || now >= end.AddSeconds(-options.CutoffSafetySeconds)) return new(false, "void_window_closed", from, until, end);
        return new(true, "eligible", from, until, end);
    }
}

public sealed record VoidReplyResult(string Outcome, string? ResultCode);
public static class VoidProtocol
{
    public static VoidReplyResult Parse(BankOperation operation, BankReply reply, VoidOperation request)
    {
        if (operation is not (BankOperation.MerchantVoid or BankOperation.EnquiryVoid)) throw new ArgumentOutOfRangeException(nameof(operation));
        if (reply.TransportError is not null || reply.StatusCode != 200) return new("Unknown", null);
        try
        {
            using var document = JsonDocument.Parse(reply.Body, new JsonDocumentOptions { MaxDepth = 8 }); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().GroupBy(x => x.Name, StringComparer.Ordinal).Any(x => x.Count() > 1)) return new("Unknown", null);
            string? Value(string key) => root.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
            var description = Value("ResDesc"); var code = Value("ResCode");
            if (Value("TransactionId") != request.TransactionId || Value("InstructionId") != request.InstructionId || Value("QRId") != request.QrId || Value("BillerNo") != request.BillerNo ||
                string.IsNullOrWhiteSpace(description) || description.Length > 100 || description.Any(char.IsControl) ||
                code is null || code.Length != 4 || code.Any(c => c is < '0' or > '9') ||
                !DateTime.TryParseExact(Value("TransDate"), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return new("Unknown", null);
            if (code == "0000") return new("Confirmed", code);
            if (operation == BankOperation.MerchantVoid)
            {
                if (code == "0002") return new("AlreadyVoided", code);
                if (code is "0003" or "0004" or "0008" or "0009") return new("Rejected", code);
            }
            // Enquiry notfound/error and duplicate-ID/internal-error responses do not authorize resending MerchantVoid.
            return new("Unknown", code);
        }
        catch (JsonException) { return new("Unknown", null); }
    }
}
