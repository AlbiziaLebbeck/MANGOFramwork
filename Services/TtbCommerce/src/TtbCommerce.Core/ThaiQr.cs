using System.Globalization;
using System.Text;

namespace TtbCommerce.Core;

public sealed record ThaiQrInput(string BillerNo, string Ref1, string Ref2, string QrId, Money Amount, string MerchantName, bool Dynamic = true);

public static class ThaiQr
{
    public static string Build(ThaiQrInput input)
    {
        if (input.BillerNo.Length != 15 || input.BillerNo.Any(c => c is < '0' or > '9')) throw new ArgumentException("Biller must be15 ASCII digits.");
        CheckReference(input.Ref1, 20); CheckReference(input.Ref2, 20); CheckReference(input.QrId, 20);
        if (input.QrId.Length != 20 || (input.Dynamic ? input.QrId[0] is not ('D' or 'Z') : input.QrId[0] != 'S')) throw new ArgumentException("QRId does not match the static/dynamic profile.");
        if (input.MerchantName.Length is < 1 or > 25 || input.MerchantName.Any(c => c is < ' ' or > '~')) throw new ArgumentException("QR merchant name must be1–25 printable ASCII characters.");
        var amount = input.Amount.ToString();
        if (input.Amount.Minor == 0 || amount.Length > 13) throw new ArgumentException("QR amount must be positive and fit the bank field.");
        var billPayment = Field("00", "A000000677010112") + Field("01", input.BillerNo) + Field("02", input.Ref1) + Field("03", input.Ref2);
        var payload = Field("00", "01") + Field("01", input.Dynamic ? "12" : "11") + Field("30", billPayment) + Field("53", "764") +
            Field("54", amount) + Field("58", "TH") + Field("59", input.MerchantName) + Field("62", Field("07", input.QrId)) + "6304";
        return payload + Crc(payload).ToString("X4", CultureInfo.InvariantCulture);
    }

    public static ushort Crc(string value)
    {
        ushort crc = 0xFFFF;
        foreach (var octet in Encoding.UTF8.GetBytes(value))
        {
            crc ^= (ushort)(octet << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    private static string Field(string tag, string value)
    {
        var length = Encoding.UTF8.GetByteCount(value);
        if (length > 99) throw new ArgumentException("TLV value is too long.");
        return tag + length.ToString("D2", CultureInfo.InvariantCulture) + value;
    }

    private static void CheckReference(string value, int maximum)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum || value.Any(c => c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9')))
            throw new ArgumentException("QR references must contain uppercase ASCII letters/digits only.");
    }
}
