namespace TtbCommerce.Core;

public sealed class ManualRefundOptions
{
    public bool ExternalRefundAttestationApproved { get; set; }
    public bool GoodsDispositionPolicyApproved { get; set; }
    public bool LocalSimulationEnabled { get; set; }
    public long MaxEvidenceBytes { get; set; } = 20 * 1024 * 1024;
}

// An attestation of an external process; never an instruction to a bank or a replacement for bank Void evidence.
public sealed class ManualRefund : Entity
{
    public CommerceEnvironment Environment { get; set; }
    public string MerchantId { get; set; } = "";
    public string BillerNo { get; set; } = "";
    public Guid BankTransactionId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid CaseId { get; set; }
    public Guid? SourceCaseId { get; set; }
    public long Version { get; set; } = 1;
    public long AmountMinor { get; set; }
    public bool AffectsOrderPayment { get; set; }
    public string State { get; set; } = "Review";
    public string CreatedBy { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? ExternalReference { get; set; }
    public DateTimeOffset? ExternalPaidAt { get; set; }
    public string EvidenceIdsJson { get; set; } = "[]";
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? DecisionReason { get; set; }
}

public sealed class FinanceEvidenceFile : Entity
{
    public Guid CaseId { get; set; }
    public string Key { get; set; } = "";
    public string FileName { get; set; } = "";
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
    public string UploadedBy { get; set; } = "";
}

public sealed class GoodsReturnLine : Entity
{
    public Guid CaseId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public int Quantity { get; set; }
    public string EffectKey { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class GoodsUnitDisposition : Entity
{
    public Guid CaseId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public int UnitIndex { get; set; }
    public Guid EntitlementId { get; set; }
    public bool Restocked { get; set; }
    public string Actor { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed record ManualRefundInput(string OperationId, string AmountMinor, string Reason, Guid? SourceCaseId = null);
public sealed record ManualRefundActionInput(string OperationId, long ExpectedVersion, string Action, string Reason,
    string? ExternalReference = null, DateTimeOffset? ExternalPaidAt = null, Guid[]? EvidenceIds = null);
public sealed record FinanceEvidenceView(Guid Id, string FileName, long ByteLength, string Sha256, string UploadedBy, DateTimeOffset UploadedAt);
public sealed record ManualRefundView(Guid Id, Guid CaseId, Guid BankTransactionId, Guid? OrderId, long Version, string State,
    string AmountMinor, string OriginalCreditMinor, bool AffectsOrderPayment, string CreatedBy, string Reason, string? SubmittedBy,
    DateTimeOffset? SubmittedAt, string? ExternalReference, DateTimeOffset? ExternalPaidAt, string? ApprovedBy, DateTimeOffset? ApprovedAt,
    string? DecisionReason, bool PolicyApproved, bool CanApprove, FinanceEvidenceView[] Files);
public sealed record ReturnedLineInput(Guid OrderLineId, int Quantity);
public sealed record DigitalUnitInput(Guid OrderLineId, int UnitIndex);
public sealed record GoodsDispositionInput(string OperationId, long ExpectedCaseVersion, string Reason, bool CancelUndelivered,
    DigitalUnitInput[] RevokeDigitalUnits, ReturnedLineInput[] Restock, bool ConsumedGoodsReviewed = false);
public sealed record GoodsLineView(Guid Id, string Name, string Kind, int PurchasedQuantity, string? DeliveryState, int? DeliveryQuantity,
    bool TrackStock, int RestockedQuantity, int MaximumRestockQuantity);
public sealed record GoodsDigitalUnitView(Guid OrderLineId, int UnitIndex, string Name, string Kind, Guid? EntitlementId, string State,
    bool AlreadyDisposed, bool DownloadRequested, bool GameUsed);
public sealed record GoodsDispositionView(Guid CaseId, long CaseVersion, Guid OrderId, string Source, bool FullRefund, bool PolicyApproved,
    string PaymentState, GoodsLineView[] Lines, GoodsDigitalUnitView[] DigitalUnits);
public sealed record GoodsDispositionResult(Guid OperationId, Guid CaseId, long CaseVersion, string State, string OrderState, int RestockedLines, int DisposedDigitalUnits);
