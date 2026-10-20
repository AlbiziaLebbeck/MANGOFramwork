using System.Text.Json.Serialization;

namespace TtbCommerce.Core;

public enum EvidenceSource { Callback, Verify, Enquiry, Mock, Report, MockVerify, MockEnquiry }
public sealed record EvidenceMetadata(EvidenceSource Source, string RequestUid, string? RequestDatetime = null, string? ServiceName = null, string? PeerIp = null);

public sealed record CreditNotice(
    string? InstructionId, string? BillerNo, string? Ref1, string? Ref2,
    [property: JsonPropertyName("QRId")] string? QrId,
    string? Amount, string? ResultCode, string? ResultDesc, string? TransDate,
    string? PayerAccount = null, string? PayerName = null, string? PayerBank = null,
    string? CrossBorderFlag = null, string? SwitchCode = null, string? CurrencyCode = null, string? SwitchFullName = null);

// Constructed only by trusted bank adapters. No HTTP endpoint binds this as a shopper request.
public sealed record CreditEvidence(string InstructionId, string BillerNo, string QrId, string Ref1, string? Ref2, long AmountMinor,
    string ResultCode, string TransDate, string? PayerAccount = null, string? PayerName = null, string? PayerBank = null,
    string? CrossBorderFlag = null, string? SwitchCode = null, string? CurrencyCode = null, string? SwitchFullName = null);

public sealed record BankAck(
    [property: JsonPropertyName("InstructionId")] string InstructionId,
    [property: JsonPropertyName("ResCode")] string ResCode,
    [property: JsonPropertyName("ResDesc")] string ResDesc,
    [property: JsonPropertyName("TransDate")] string TransDate);

public sealed record CreditApplication(BankAck Ack, Guid? BankTransactionId, Guid? OrderId, string Outcome);
