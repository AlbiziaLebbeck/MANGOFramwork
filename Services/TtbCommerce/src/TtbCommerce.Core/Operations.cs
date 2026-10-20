namespace TtbCommerce.Core;

public sealed class OperationsOptions
{
    public bool RecoveryFreeze { get; set; }
    public bool MetricsEnabled { get; set; }
    public string MetricsToken { get; set; } = "";
    // Short UAT-only observation window; never enables a bank callback or publishes an address.
    public DateTimeOffset? TransportProbeUntilUtc { get; set; }
    // An explicitly agreed operational deadline; a bare RSA PEM has no certificate NotAfter.
    public DateTimeOffset? BankSigningKeyRotationDueUtc { get; set; }
}

public sealed class BankResilienceOptions
{
    public int FailureThreshold { get; set; } = 5;
    public int BreakSeconds { get; set; } = 30;
}
