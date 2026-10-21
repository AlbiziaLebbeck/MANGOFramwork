using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class CheckoutReadinessTests : IDisposable
{
    private readonly string key = Path.Combine(Path.GetTempPath(), $"ttb-checkout-test-{Guid.NewGuid():N}.pem");

    public CheckoutReadinessTests()
    {
        using var rsa = RSA.Create(2048);
        File.WriteAllText(key, rsa.ExportPkcs8PrivateKeyPem());
    }

    private TtbOptions Bank() => new()
    {
        MerchantId = "TST", BillerNo = "000000000000001", Issuer = "unit-issuer", PrivateKeyPath = key,
        QrProfileApproved = true, GatewayAuthenticationValidated = true, BeneficiaryName = "UAT TEST",
        CallbackProfileApproved = true, CallbackAuthentication = "Basic", CallbackUsername = "unit", CallbackPassword = "unit",
        CallbackSourceIpValidated = false, UatTestCheckoutEnabled = true
    };
    private static CommerceOptions Uat() => new() { Environment = CommerceEnvironment.Uat, PaymentMode = PaymentMode.Bank };

    [Fact]
    public void Explicit_UAT_test_checkout_does_not_claim_first_callback_acceptance()
    {
        var readiness = new BankReadiness(Uat(), Bank());
        Assert.Empty(readiness.MissingForCheckout());
        readiness.AssertCheckoutReady();
        Assert.Contains("Ttb.CallbackSourceIpValidation", readiness.Missing());
        Assert.Throws<CommerceException>(() => readiness.AssertReady());
    }

    [Fact]
    public void UAT_remains_closed_without_explicit_test_checkout_setting()
    {
        var bank = Bank(); bank.UatTestCheckoutEnabled = false;
        var readiness = new BankReadiness(Uat(), bank);
        Assert.Contains("Ttb.CallbackSourceIpValidation", readiness.MissingForCheckout());
        Assert.Throws<CommerceException>(() => readiness.AssertCheckoutReady());
    }

    [Fact]
    public void Bank_source_profile_can_open_test_checkout_without_claiming_actual_callback_acceptance()
    {
        var bank = Bank();
        bank.CallbackAuthentication = "BankSourceIp";
        bank.CallbackUsername = ""; bank.CallbackPassword = "";
        var readiness = new BankReadiness(Uat(), bank);
        Assert.Empty(readiness.MissingForCheckout());
        Assert.Contains("Ttb.CallbackSourceIpValidation", readiness.Missing());
        Assert.Throws<CommerceException>(() => readiness.AssertReady());
    }

    [Fact]
    public void UAT_setting_cannot_relax_Production_callback_or_report_acceptance()
    {
        var readiness = new BankReadiness(new() { Environment = CommerceEnvironment.Production, PaymentMode = PaymentMode.Bank }, Bank());
        Assert.Contains("Ttb.CallbackSourceIpValidation", readiness.MissingForCheckout());
        Assert.Contains("Reports.ProductionMandatoryProfile", readiness.MissingForCheckout());
        Assert.Throws<CommerceException>(() => readiness.AssertCheckoutReady());
    }

    [Theory]
    [InlineData("gateway", "Ttb.GatewayAuthenticationValidation")]
    [InlineData("callback-auth", "Ttb.CallbackAuthenticationProfile")]
    [InlineData("qr-profile", "Ttb.QrProfileApproval")]
    [InlineData("beneficiary", "Ttb.BeneficiaryName")]
    [InlineData("signing-key", "Ttb.SigningPrivateKey")]
    public void UAT_test_checkout_still_requires_authenticated_gateway_and_valid_profile(string field, string missing)
    {
        var bank = Bank();
        switch (field)
        {
            case "gateway": bank.GatewayAuthenticationValidated = false; break;
            case "callback-auth": bank.CallbackPassword = ""; break;
            case "qr-profile": bank.QrProfileApproved = false; break;
            case "beneficiary": bank.BeneficiaryName = ""; break;
            case "signing-key": bank.PrivateKeyPath = key + ".missing"; break;
        }
        var readiness = new BankReadiness(Uat(), bank);
        Assert.Contains(missing, readiness.MissingForCheckout());
        Assert.Throws<CommerceException>(() => readiness.AssertCheckoutReady());
    }

    [PgFact]
    public async Task First_UAT_order_can_issue_a_QR_while_remaining_unpaid_and_unfulfilled()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await CartTests.ShopperAsync(db, "uat-first-checkout");
        var product = await CatalogTests.PublishedAsync(db);
        var quote = await OrderTests.QuoteAsync(db, actor, (product.Variant.Id, 1));
        var options = Uat(); var bank = Bank(); var readiness = new BankReadiness(options, bank);
        var orders = new OrderService(db, new CartService(db, options, TimeProvider.System), new(db, TimeProvider.System), options, TimeProvider.System, readiness);
        var order = await orders.CreateAsync(actor.ShopperId, new(quote.Id), "uat-first-qr", default);
        var qr = await new QrService(db, new(db, TimeProvider.System), options, bank, readiness, TimeProvider.System)
            .GetOrCreateAsync(actor.ShopperId, order.Id, default);
        Assert.StartsWith("data:image/png;base64,", qr.ImageDataUrl);
        Assert.Equal(order.TotalMinor, qr.AmountMinor);
        Assert.Equal("Bank", qr.Mode);
        Assert.Equal("UAT TEST", qr.BeneficiaryName);
        Assert.Equal(PaymentState.Unpaid, (await db.Orders.SingleAsync()).PaymentState);
        Assert.Equal(0, await db.BankTransactions.CountAsync());
        Assert.Equal(0, await db.Allocations.CountAsync());
        Assert.Equal(0, await db.Fulfillments.CountAsync());
        Assert.Equal(0, await db.Documents.CountAsync());
        Assert.False(bank.CallbackSourceIpValidated);
    }

    public void Dispose() => File.Delete(key);
}
