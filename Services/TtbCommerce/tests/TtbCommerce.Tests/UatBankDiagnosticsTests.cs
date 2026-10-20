using System.Globalization;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class UatBankDiagnosticsTests
{
    [Fact]
    public void Diagnostic_uses_confirmed_profile_Bangkok_expiry_and_fixed_one_baht_without_fake_order()
    {
        var bank = new TtbOptions { MerchantId = "PYA", BillerNo = "075555900002101", Issuer = "jwt-key-pya", QrProfileApproved = true };
        var expiry = DateTimeOffset.Parse("2026-10-20T04:05:00Z", CultureInfo.InvariantCulture);
        var run = Guid.Parse("4c7665a9-dde4-4b3a-b2ae-3114ba706f02");
        var qr = UatBankDiagnostics.Build(bank, expiry, 42, run);
        Assert.Equal("ZPYAE261020110500042", qr.QrId);
        Assert.Equal(20, qr.Ref1.Length); Assert.Equal(20, qr.Ref2.Length);
        Assert.StartsWith("TTBUAT", qr.Ref1);
        Assert.Equal(ThaiQr.Build(new(bank.BillerNo, qr.Ref1, qr.Ref2, qr.QrId, new Money(100), bank.QrMerchantName)), qr.Payload);
        Assert.StartsWith("data:image/png;base64,iVBOR", qr.ImageDataUrl);
        UatBankDiagnostics.AssertProfile(new() { Environment = CommerceEnvironment.Uat, PaymentMode = PaymentMode.Bank }, bank);
        Assert.Throws<InvalidOperationException>(() => UatBankDiagnostics.AssertProfile(new(), bank));
        Assert.Throws<InvalidOperationException>(() => UatBankDiagnostics.AssertProfile(new() { Environment = CommerceEnvironment.Production, PaymentMode = PaymentMode.Bank }, bank));
        Assert.Throws<ArgumentOutOfRangeException>(() => UatBankDiagnostics.Build(bank, expiry, 100000, run));
    }
}
