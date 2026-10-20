namespace TtbCommerce.Core;

public enum CommerceEnvironment { Local, Uat, Production }
public enum PaymentMode { Mock, Bank }
public enum RuntimeRole { All, Api, CommerceWorker, ReceiptWorker }

public sealed class CommerceOptions
{
    public CommerceEnvironment Environment { get; set; } = CommerceEnvironment.Local;
    public PaymentMode PaymentMode { get; set; } = PaymentMode.Mock;
    public RuntimeRole Role { get; set; } = RuntimeRole.All;
    public string PathPrefix { get; set; } = "/ttb-commerce/v1";
    public int PaymentMinutes { get; set; } = 10;
    public int QuoteMinutes { get; set; } = 5;
    public bool PickupEnabled { get; set; } = true;
    public bool ShippingEnabled { get; set; }
    public long ShippingFeeMinor { get; set; }
    public int MaxCartLines { get; set; } = 30;
    public long MaxOrderMinor { get; set; } = 999999999999;
    public int VerificationDelaySeconds { get; set; } = 60;
    public int VerificationCooldownSeconds { get; set; } = 30;
    public int VerificationBudget { get; set; } = 3;
    public int BankReadsPerMinute { get; set; } = 30;
    public bool LocalTestIdentityEnabled { get; set; }
    public string MangosUserUrl { get; set; } = "https://api.mangosgo.com/api/v1/users/me";
    public string[] TrustedOrigins { get; set; } = [];
    public string[] AdminSubjects { get; set; } = [];
    public string[] FinanceSubjects { get; set; } = [];
    public string[] FulfillmentSubjects { get; set; } = [];
    public string[] MangosCookieNames { get; set; } = [];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(Environment) || !Enum.IsDefined(PaymentMode) || !Enum.IsDefined(Role)) errors.Add("Unknown deployment environment, payment mode or runtime role.");
        if (!PathPrefix.StartsWith('/') || PathPrefix.Contains('?', StringComparison.Ordinal) || PathPrefix.EndsWith('/'))
            errors.Add("PathPrefix must be an absolute URL path without trailing slash or query.");
        if (PaymentMinutes is < 1 or > 60) errors.Add("PaymentMinutes must be between 1 and 60.");
        if (QuoteMinutes is < 1 or > 15 || MaxCartLines is < 1 or > 100 || ShippingFeeMinor < 0 || MaxOrderMinor is < 1 or > 999999999999)
            errors.Add("Quote, cart or shipping policy is invalid.");
        if (VerificationDelaySeconds < 60 || VerificationCooldownSeconds < 1 || VerificationBudget is < 1 or > 3)
            errors.Add("Verification policy must wait at least 60 seconds and allow no more than three bank checks.");
        if (BankReadsPerMinute is < 1 or > 600) errors.Add("Bank read rate policy is invalid.");
        if (Environment == CommerceEnvironment.Production && PaymentMode == PaymentMode.Mock)
            errors.Add("Mock payments are prohibited in Production.");
        if (Environment == CommerceEnvironment.Uat && PaymentMode != PaymentMode.Bank || Environment == CommerceEnvironment.Local && PaymentMode != PaymentMode.Mock)
            errors.Add("Local requires Mock payments and UAT requires Bank payments; profiles cannot be mixed.");
        if (Environment != CommerceEnvironment.Local && LocalTestIdentityEnabled)
            errors.Add("Local test identities are prohibited outside Local.");
        if (!Uri.TryCreate(MangosUserUrl, UriKind.Absolute, out var identity) || identity.Scheme != Uri.UriSchemeHttps)
            errors.Add("MANGOs identity URL must use HTTPS.");
        foreach (var origin in TrustedOrigins)
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0 ||
                (uri.Scheme != Uri.UriSchemeHttps && !(Environment == CommerceEnvironment.Local && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp)))
                errors.Add("TrustedOrigins must contain exact HTTPS origins (local HTTP loopback only in Local).");
        return errors;
    }
}

public sealed class TtbOptions
{
    public string MerchantId { get; set; } = "";
    public string BillerNo { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public int TokenSeconds { get; set; } = 120;
    public string SenderName { get; set; } = "";
    public string QrMerchantName { get; set; } = "MANGOSGO";
    public string BeneficiaryName { get; set; } = "";
    public bool CallbackProfileApproved { get; set; }
    public bool CallbackSourceIpValidated { get; set; }
    public string[] TrustedProxyCidrs { get; set; } = [];
    public bool QrProfileApproved { get; set; }
    public bool CrossBorderApproved { get; set; }
    public string CallbackAuthentication { get; set; } = "Unavailable";
    public string CallbackUsername { get; set; } = "";
    public string CallbackPassword { get; set; } = "";
    public string[] BankSourceIps { get; set; } = ["203.148.142.200", "203.148.143.101", "203.148.143.102", "203.148.143.103", "203.148.143.104", "203.148.143.105"];

    public IReadOnlyList<string> MissingBankRequirements()
    {
        var missing = new List<string>();
        if (MerchantId.Length != 3 || MerchantId.Any(c => c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9')))
            missing.Add("Ttb.MerchantId");
        if (BillerNo.Length != 15 || BillerNo.Any(c => c is < '0' or > '9')) missing.Add("Ttb.BillerNo");
        if (string.IsNullOrWhiteSpace(Issuer)) missing.Add("Ttb.Issuer");
        if (string.IsNullOrWhiteSpace(PrivateKeyPath)) missing.Add("Ttb.PrivateKeyPath");
        if (TokenSeconds is < 1 or > 1200) missing.Add("Ttb.TokenSeconds");
        if (SenderName.Length > 20 || SenderName.Any(c => c is < ' ' or > '~')) missing.Add("Ttb.SenderName");
        if (!CallbackProfileApproved || CallbackAuthentication != "Basic" || string.IsNullOrEmpty(CallbackUsername) || string.IsNullOrEmpty(CallbackPassword))
            missing.Add("Ttb.CallbackAuthenticationProfile");
        if (!QrProfileApproved) missing.Add("Ttb.QrProfileApproval");
        if (!CallbackSourceIpValidated) missing.Add("Ttb.CallbackSourceIpValidation");
        if (string.IsNullOrWhiteSpace(BeneficiaryName)) missing.Add("Ttb.BeneficiaryName");
        if (QrMerchantName.Length is < 1 or > 25 || QrMerchantName.Any(c => c is < ' ' or > '~')) missing.Add("Ttb.QrMerchantName");
        return missing;
    }

    public static Uri Gateway(CommerceEnvironment environment) => environment switch
    {
        CommerceEnvironment.Uat => new("https://uat-dgw.tau2904.com:9643"),
        CommerceEnvironment.Production => new("https://dgw.ttbbank.com:9643"),
        _ => throw new InvalidOperationException("Local mode cannot select a real bank gateway.")
    };
}
