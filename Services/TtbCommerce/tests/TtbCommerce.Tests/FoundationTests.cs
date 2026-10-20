using TtbCommerce.Core;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class FoundationTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("0.01", 1)]
    [InlineData("00111.75", 11175)]
    [InlineData("10.1", 1010)]
    [InlineData("199.78", 19978)]
    public void Money_parser_is_exact(string text, long expected) => Assert.Equal(expected, Money.Parse(text).Minor);

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1,000.00")]
    [InlineData("1e2")]
    [InlineData(" 10")]
    [InlineData("10 ")]
    [InlineData("1.001")]
    [InlineData("1.")]
    [InlineData(".10")]
    [InlineData("๑๐.๐๐")]
    [InlineData("92233720368547758.08")]
    public void Money_parser_rejects_ambiguous_or_overflowing_input(string text) => Assert.Throws<FormatException>(() => Money.Parse(text));

    [Fact]
    public void Money_arithmetic_cannot_silently_overflow()
    {
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue).Add(new Money(1)));
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue).Multiply(2));
        Assert.Equal("0.01", new Money(1).ToString());
        Assert.Equal("111.75", new Money(11175).ToString());
    }

    [Fact]
    public void Production_cannot_run_mock_payments() => Assert.NotEmpty(new CommerceOptions { Environment = CommerceEnvironment.Production }.Validate());

    [Fact]
    public void Local_and_bank_profiles_cannot_be_mixed()
    {
        Assert.NotEmpty(new CommerceOptions { Environment = CommerceEnvironment.Uat, PaymentMode = PaymentMode.Mock }.Validate());
        Assert.NotEmpty(new CommerceOptions { Environment = CommerceEnvironment.Local, PaymentMode = PaymentMode.Bank }.Validate());
    }

    [Fact]
    public void Local_credentials_cannot_leak_to_another_environment() => Assert.NotEmpty(new CommerceOptions
    {
        Environment = CommerceEnvironment.Uat, PaymentMode = PaymentMode.Bank, LocalTestIdentityEnabled = true
    }.Validate());

    [Fact]
    public void Verify_budget_respects_bank_restrictions() => Assert.NotEmpty(new CommerceOptions { VerificationBudget = 4 }.Validate());

    [Fact]
    public void Missing_bank_profile_does_not_become_ready() => Assert.Contains("Ttb.CallbackAuthenticationProfile", new TtbOptions().MissingBankRequirements());

    [Fact]
    public void Local_cannot_fall_back_to_a_real_bank() => Assert.Throws<InvalidOperationException>(() => TtbOptions.Gateway(CommerceEnvironment.Local));

    [Fact]
    public void Origins_cannot_contain_paths_or_insecure_public_hosts()
    {
        Assert.NotEmpty(new CommerceOptions { TrustedOrigins = ["https://plus.mangosgo.com/path"] }.Validate());
        Assert.NotEmpty(new CommerceOptions { TrustedOrigins = ["http://plus.mangosgo.com"] }.Validate());
        Assert.Empty(new CommerceOptions { TrustedOrigins = ["http://localhost:5173"] }.Validate());
    }
}
