namespace TtbCommerce.Core;

public enum VoidWindowPolicy { Unknown, SameCalendarDayBefore2256, BankingDay2256 }
public sealed class VoidOptions
{
    public bool Enabled { get; set; }
    public bool ProfileApproved { get; set; }
    public bool LocalSimulationEnabled { get; set; }
    public bool FulfilledRefundPolicyApproved { get; set; }
    public VoidWindowPolicy WindowPolicy { get; set; }
    public int CutoffSafetySeconds { get; set; } = 30;
    public int EnquiryBudget { get; set; } = 3;
    public int EnquiryCooldownSeconds { get; set; } = 30;
    public bool ValidPolicy => Enum.IsDefined(WindowPolicy) && WindowPolicy != VoidWindowPolicy.Unknown && CutoffSafetySeconds is >= 0 and <= 120 && EnquiryBudget is >= 1 and <= 3 && EnquiryCooldownSeconds is >= 15 and <= 300;
}

public sealed class VoidOperation : Entity
{
    public CommerceEnvironment Environment { get; set; }
    public string MerchantId { get; set; } = "";
    public Guid? OrderId { get; set; }
    public bool AffectsOrderPayment { get; set; } = true;
    public Guid BankTransactionId { get; set; }
    public string OperationKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public string InstructionId { get; set; } = "";
    public string QrId { get; set; } = "";
    public string BillerNo { get; set; } = "";
    public long AmountMinor { get; set; }
    public string Actor { get; set; } = "";
    public string OperatorName { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool DeliveryReviewed { get; set; }
    public string PolicyJson { get; set; } = "{}";
    public string State { get; set; } = "Requested";
    public string? ResultCode { get; set; }
    public DateTimeOffset? SendFencedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmationSource { get; set; }
    public int EnquiryCount { get; set; }
    public DateTimeOffset? LastEnquiryAt { get; set; }
    public Guid? EnquiryLeaseToken { get; set; }
    public DateTimeOffset? EnquiryLeaseUntil { get; set; }
}

public sealed class VoidBankRequest : Entity
{
    public Guid VoidOperationId { get; set; }
    public string Service { get; set; } = "";
    public string RequestUid { get; set; } = "";
    public string RequestJson { get; set; } = "{}";
    public string Outcome { get; set; } = "Reserved";
    public string? ResultCode { get; set; }
    public int? HttpStatus { get; set; }
    public byte[] RawResponse { get; set; } = [];
    public string? ResponseSha256 { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class MockVoidCredit : Entity
{
    public string InstructionId { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public string BillerNo { get; set; } = "";
    public string QrId { get; set; } = "";
}

public sealed record VoidInput(string OperationId, string Reason, bool DeliveryReviewed = false);
public sealed record VoidView(Guid Id, Guid? OrderId, string TransactionId, string State, string? ResultCode, int EnquiriesUsed, int EnquiriesRemaining,
    DateTimeOffset RequestedAt, DateTimeOffset? ConfirmedAt, string? ConfirmationSource);
public sealed record VoidEligibility(bool Eligible, string Code, DateTimeOffset? EarliestPaymentAt = null, DateTimeOffset? LatestPaymentAt = null, DateTimeOffset? WindowEndsAt = null);
public sealed record VoidReview(Guid BankTransactionId, Guid? OrderId, string AmountMinor, bool AffectsOrderPayment, bool DeliveryReviewRequired,
    bool FulfilledPolicyApproved, VoidEligibility Eligibility, VoidView[] Operations, DocumentView[] Documents);
