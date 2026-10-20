using System.Security.Cryptography;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed record BankSigningKeyStatus(bool Expected, bool Usable, bool RotationScheduled, long? RotationDueUnixSeconds);

public static class BankSigningKeyProbe
{
    public static BankSigningKeyStatus Read(CommerceOptions commerce, TtbOptions bank, OperationsOptions operations)
    {
        var expected = commerce.PaymentMode == PaymentMode.Bank && commerce.Environment is CommerceEnvironment.Uat or CommerceEnvironment.Production &&
            !string.IsNullOrWhiteSpace(bank.Issuer) && !string.IsNullOrWhiteSpace(bank.PrivateKeyPath);
        var due = operations.BankSigningKeyRotationDueUtc?.ToUnixTimeSeconds();
        var scheduled = expected && due is > 0;
        return new(expected, expected && Usable(bank.PrivateKeyPath), scheduled, scheduled ? due : null);
    }

    internal static bool Usable(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var key = BankJwtSigner.ReadKey(path);
            if (key.KeySize != 2048) return false;
            var parameters = key.ExportParameters(true);
            try { return parameters.D is { Length: > 0 }; }
            finally
            {
                foreach (var bytes in new[] { parameters.D, parameters.DP, parameters.DQ, parameters.P, parameters.Q, parameters.InverseQ })
                    if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException) { return false; }
    }
}
