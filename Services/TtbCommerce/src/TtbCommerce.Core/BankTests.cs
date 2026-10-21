namespace TtbCommerce.Core;

public sealed class BankTestOptions
{
    public bool Enabled { get; set; }
    public string Reference1 { get; set; } = "TEST";
    public long MaximumAmountMinor { get; set; } = 10000;
    public int ReadBudget { get; set; } = 10;
    public bool VoidEnabled { get; set; }
    public bool ValidProfile() => Reference1.Length is >= 1 and <= 20 &&
        Reference1.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)) &&
        !(Reference1.Length == 15 && Reference1[0] == 'T' && Reference1[1..].All(char.IsAsciiDigit)) &&
        MaximumAmountMinor is >= 1 and <= 1000000 && ReadBudget is >= 1 and <= 30;
}

// These records never reference shopper orders, allocations, inventory or receipts.
public sealed class BankTestCase : Entity
{
    public string MerchantId { get; set; } = "";
    public string BillerNo { get; set; } = "";
    public string Ref1 { get; set; } = "TEST";
    public string Ref2 { get; set; } = "";
    public string QrId { get; set; } = "";
    public string Payload { get; set; } = "";
    public long AmountMinor { get; set; }
    public string Origin { get; set; } = "ConsoleQr";
    public string State { get; set; } = "AwaitingPayment";
    public string? InstructionId { get; set; }
    public bool CrossBorder { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? PaymentObservedAt { get; set; }
    public DateTimeOffset? LastRequestAt { get; set; }
}

public sealed class BankTestEvent : Entity
{
    public Guid? CaseId { get; set; }
    public Guid? OperationId { get; set; }
    public string Kind { get; set; } = "Notification";
    public string Outcome { get; set; } = "Pending";
    public string Ref1 { get; set; } = "";
    public string Ref2 { get; set; } = "";
    public string QrId { get; set; } = "";
    public string InstructionId { get; set; } = "";
    public long? AmountMinor { get; set; }
    public string RequestUid { get; set; } = "";
    public int? HttpStatus { get; set; }
    public string? BankCode { get; set; }
    public string PayloadHash { get; set; } = "";
    public byte[] RawPayload { get; set; } = [];
    public string PayloadJson { get; set; } = "{}";
    public string ResponseJson { get; set; } = "{}";
    public string MetadataJson { get; set; } = "{}";
}

public sealed class BankTestReceipt : Entity
{
    public Guid? CaseId { get; set; }
    public string MerchantId { get; set; } = "";
    public string InstructionId { get; set; } = "";
    public string BusinessHash { get; set; } = "";
    public string AckCode { get; set; } = "";
    public int DeliveryCount { get; set; } = 1;
    public DateTimeOffset LastReceivedAt { get; set; }
}

public sealed class BankTestOperation : Entity
{
    public Guid CaseId { get; set; }
    public string OperationKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public string RequestUid { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public string InstructionId { get; set; } = "";
    public string RequestJson { get; set; } = "{}";
    public string Outcome { get; set; } = "Unknown";
    public string? ResultCode { get; set; }
    public int? HttpStatus { get; set; }
    public string? TransportError { get; set; }
    public DateTimeOffset SendFencedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed record BankTestCreateInput(string Amount = "1.00");
public sealed record BankTestActionInput(string OperationId, string? ConfirmReference = null);
public sealed record BankTestCaseView(Guid Id, string Ref1, string Ref2, string QrId, long AmountMinor, string State,
    string Origin, string? InstructionId, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, string BeneficiaryName,
    string? ImageDataUrl, int ReadChecksUsed, bool VoidEnabled);
public sealed record BankTestEventView(Guid Id, Guid? CaseId, Guid? OperationId, string Kind, string Outcome, string Ref1,
    string QrId, string InstructionId, long? AmountMinor, string RequestUid, int? HttpStatus, string? BankCode,
    string PayloadHash, DateTimeOffset CreatedAt, string PayloadJson, string ResponseJson);
public sealed record BankTestOperationView(Guid Id, Guid CaseId, string Kind, string RequestUid, string TransactionId,
    string Outcome, string? ResultCode, int? HttpStatus, string? TransportError, DateTimeOffset SendFencedAt, DateTimeOffset? CompletedAt);
