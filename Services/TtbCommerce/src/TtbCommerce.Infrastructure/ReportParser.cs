using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public static partial class ReportParser
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    [GeneratedRegex("\\A(BPQR_ISO|BPQRLOST)_([0-9]{6})\\.TXT\\z", RegexOptions.CultureInvariant)]
    private static partial Regex FilePattern();

    public static ReportFileName FileName(string name)
    {
        var match = FilePattern().Match(name);
        if (!match.Success || !DateOnly.TryParseExact("20" + match.Groups[2].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw Error("filename");
        return new(name, match.Groups[1].Value == "BPQR_ISO" ? ReportKind.Iso : ReportKind.Lost, date);
    }

    public static ParsedReport Parse(byte[] raw, string fileName, ReportOptions profile)
    {
        if (!profile.ProfileApproved || profile.Layout == ReportLayout.Unknown || !Enum.IsDefined(profile.Layout) ||
            profile.Width == ReportWidth.Unknown || !Enum.IsDefined(profile.Width) || profile.CreditTotal == ReportCreditTotal.Unknown || !Enum.IsDefined(profile.CreditTotal) || !profile.FileDateIsHeaderDate)
            throw Error("profile_unapproved");
        if (raw.Length > profile.MaxFileBytes || raw.Length == 0) throw Error("size");
        var file = FileName(fileName);
        string text;
        try { text = Utf8.GetString(raw); } catch (DecoderFallbackException) { throw Error("utf8"); }
        if (text.StartsWith('\uFEFF')) text = text[1..];
        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n'))) throw Error("control_character");
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Contains('\r')) throw Error("line_ending");
        if (text.EndsWith('\n')) text = text[..^1];
        var lines = text.Split('\n');
        if (lines.Length is < 2 or > 999999) throw Error("record_count");
        var header = new Fields(lines[0], profile.Width); var trailer = new Fields(lines[^1], profile.Width);
        if (header.At(1, 1) != "H" || trailer.At(1, 1) != "T") throw Error("header_trailer");
        if (header.Number(2, 6) != 1 || trailer.Number(2, 6) != lines.Length - (profile.SequenceIncludesTrailer ? 0 : 1)) throw Error("sequence");
        var merchant = header.At(8, 3);
        if (merchant.Length != 3 || merchant.Any(c => c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9'))) throw Error("merchant");
        var date = Date(header.At(11, 8));
        if (date != file.Date) throw Error("file_date");
        header.Blank(19, 482);
        var details = new List<ReportDetail>();
        for (var index = 1; index < lines.Length - 1; index++)
        {
            var row = new Fields(lines[index], profile.Width);
            if (row.Number(2, 6) != index + 1) throw Error("sequence");
            if (row.At(1, 1) != "D") throw Error("detail_type");
            var kind = row.At(279, 1); var lump = row.At(280, 1); var amount = row.Number(281, 15);
            if (kind is not ("C" or "D" or "V") || lump is not ("Y" or "N") || amount <= 0) throw Error("transaction");
            var instruction = row.Ascii(8, 35, required: true); var original = row.Ascii(43, 35);
            if (kind == "D" && original.Length == 0 || kind == "C" && original.Length > 0) throw Error("original_instruction");
            var biller = row.Ascii(300, 20, true);
            if (biller.Length != 15 || biller.Any(c => c is < '0' or > '9')) throw Error("biller");
            var paymentAt = Timestamp(row.At(201, 8), row.At(209, 6));
            DateTimeOffset? creditAt = file.Kind == ReportKind.Lost ? Timestamp(row.At(320, 8), row.At(328, 6)) : null;
            if (creditAt < paymentAt) throw Error("lost_credit_time");
            var cross = ""; var code = ""; var currency = ""; var fullName = "";
            var offset = file.Kind == ReportKind.Lost ? 334 : 320;
            if (profile.Layout == ReportLayout.CrossBorder301)
            {
                cross = row.At(offset, 1); code = row.Ascii(offset + 1, 6); currency = row.Ascii(offset + 7, 3); fullName = row.At(offset + 10, 100);
                if (cross is not ("Y" or "N") || cross == "Y" && (code.Length == 0 || currency.Length != 3 || fullName.Length == 0) ||
                    cross == "N" && (code.Length > 0 || currency.Length > 0 || fullName.Length > 0)) throw Error("cross_border");
                row.Blank(offset + 110, 501 - (offset + 110));
            }
            else row.Blank(offset, 501 - offset);
            details.Add(new((int)row.Number(2, 6), instruction, original, row.Ascii(78, 3), row.Ascii(81, 20), row.At(101, 40), row.At(141, 40),
                row.Ascii(181, 20, true), paymentAt, row.Ascii(215, 20, true), row.Ascii(235, 20), row.Ascii(255, 20, true),
                row.Ascii(275, 4), kind, lump, amount, row.Number(296, 4), biller, creditAt, cross, code, currency, fullName));
        }
        if (details.GroupBy(x => (x.InstructionId, x.TransactionKind)).Any(x => x.Count() > 1)) throw Error("duplicate_movement");
        trailer.Blank(50, 451);
        var debit = details.Where(x => x.TransactionKind == "D").ToArray(); var voided = details.Where(x => x.TransactionKind == "V").ToArray();
        var credit = details.Where(x => x.TransactionKind == "C" || profile.CreditTotal == ReportCreditTotal.IncludesVoidedOriginals && x.TransactionKind == "V").ToArray();
        ReportTotals totals;
        try { totals = new(debit.Sum(x => x.AmountMinor), debit.Length, credit.Sum(x => x.AmountMinor), credit.Length, voided.Sum(x => x.AmountMinor), voided.Length); }
        catch (OverflowException) { throw Error("totals_overflow"); }
        if (totals.DebitMinor != trailer.Number(8, 15) || totals.DebitCount != trailer.Number(23, 6) || totals.CreditMinor != trailer.Number(29, 15) || totals.CreditCount != trailer.Number(44, 6)) throw Error("totals");
        return new(merchant, date, file.Kind, details.ToArray(), totals);
    }

    private static DateOnly Date(string value) => DateOnly.TryParseExact(value, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw Error("date");
    private static DateTimeOffset Timestamp(string date, string time) => DateTime.TryParseExact(date + time, "ddMMyyyyHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
        ? new DateTimeOffset(value, TimeSpan.FromHours(7)) : throw Error("timestamp");
    public static CommerceException Error(string code) => new(422, "report_" + code, "The bank report is quarantined; inspect its recorded validation code.");

    private sealed class Fields
    {
        private readonly ReportWidth width; private readonly byte[] bytes; private readonly Rune[] runes;
        public Fields(string line, ReportWidth width)
        {
            this.width = width; bytes = Utf8.GetBytes(line); runes = line.EnumerateRunes().ToArray();
            if ((width == ReportWidth.Utf8Bytes ? bytes.Length : runes.Length) != 500) throw Error("record_width");
        }
        public string At(int start, int length)
        {
            try { return (width == ReportWidth.Utf8Bytes ? Utf8.GetString(bytes, start - 1, length) : string.Concat(runes.Skip(start - 1).Take(length).Select(x => x.ToString()))).TrimEnd(' '); }
            catch (DecoderFallbackException) { throw Error("field_utf8_boundary"); }
        }
        public string Ascii(int start, int length, bool required = false)
        {
            var value = At(start, length);
            if (value.Any(c => c is < '!' or > '~') || required && value.Length == 0) throw Error("identifier");
            return value;
        }
        public long Number(int start, int length)
        {
            var value = At(start, length);
            if (value.Length != length || value.Any(c => c is < '0' or > '9') || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)) throw Error("number");
            return result;
        }
        public void Blank(int start, int length) { if (At(start, length).Length != 0) throw Error("reserved_field"); }
    }
}
