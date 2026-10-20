namespace TtbCommerce.Core;

public sealed class FinanceCaseNote : Entity
{
    public Guid CaseId { get; set; }
    public string Actor { get; set; } = "";
    public string Body { get; set; } = "";
}
public sealed class FinanceOperation : Entity
{
    public Guid? CaseId { get; set; }
    public Guid? OrderId { get; set; }
    public string OperationKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "{}";
    public string OutcomeJson { get; set; } = "{}";
}
public sealed record CaseActionInput(string OperationId, long ExpectedVersion, string Action, string Reason);
public sealed record CaseSummary(Guid Id, long Version, string Kind, string State, Guid? OrderId, string? OrderNumber, Guid? BankTransactionId,
    string? AmountMinor, string? InstructionId, string? Assignee, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt);
public sealed record CaseNoteView(Guid Id, string Actor, string Body, DateTimeOffset CreatedAt);
public sealed record FinanceBankView(Guid Id, string InstructionId, string QrId, string BillerNo, string Ref1, string Ref2, string AmountMinor,
    bool Voided, string? CrossBorderFlag, string? CurrencyCode, DateTimeOffset? OriginalCreditAt, string TimeSource, Guid? AllocatedOrderId, bool? Primary,
    string ManualAttestedMinor = "0", string ManualReservedMinor = "0");
public sealed record FinanceCaseDetail(CaseSummary Case, FinanceBankView? Bank, string EvidenceJson, string? ResolutionJson, CaseNoteView[] Notes, ManualRefundView? ManualRefund = null);
public sealed record FinanceCasePage(CaseSummary[] Items, Guid? NextCursor);
public sealed record FinanceActionResult(Guid OperationId, Guid CaseId, long Version, string State, string Action);
public sealed record AdminOrderSummary(Guid Id, string Number, string State, string PaymentState, string BuyerName, string TotalMinor, DateTimeOffset CreatedAt);
public sealed record AdminOrderPage(AdminOrderSummary[] Items, Guid? NextCursor);
