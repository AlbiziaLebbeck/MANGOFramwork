namespace TtbCommerce.Core;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DeploymentIdentity : Entity
{
    public string Key { get; set; } = "singleton";
    public CommerceEnvironment Environment { get; set; }
}

public sealed class Shopper : Entity
{
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class ShopperSession : Entity
{
    public Guid ShopperId { get; set; }
    public string TokenHash { get; set; } = "";
    public string CsrfHash { get; set; } = "";
    public string IdentityFingerprint { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public enum ProductKind { Physical, Digital }
public enum DigitalDeliveryKind { InventoryItem, Download }
public sealed class Product : Entity
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ProductKind Kind { get; set; }
    public bool Published { get; set; }
    public long Version { get; set; } = 1;
    public string PreviewJson { get; set; } = "{}";
    public string ThumbnailUrl { get; set; } = "";
    public string TaxCode { get; set; } = "";
}

public sealed class ProductVariant : Entity
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public long PriceMinor { get; set; }
    public bool Active { get; set; } = true;
    public bool TrackStock { get; set; }
    public int OnHand { get; set; }
    public int Reserved { get; set; }
    public string OptionsJson { get; set; } = "{}";
    public string? DigitalAssetKey { get; set; }
    public DigitalDeliveryKind DigitalKind { get; set; }
}

public sealed class Cart : Entity
{
    public Guid ShopperId { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CartLine : Entity
{
    public Guid CartId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
}

public sealed class Quote : Entity
{
    public Guid ShopperId { get; set; }
    public Guid CartId { get; set; }
    public long CartVersion { get; set; }
    public long SubtotalMinor { get; set; }
    public long ShippingMinor { get; set; }
    public long TotalMinor { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string SnapshotHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? ConsumedByOrderId { get; set; }
}

public enum OrderState { Open, CancelRequested, Cancelled, Completed, Review }
public enum PaymentState { Unpaid, Paid, Review, VoidPending, Voided, Refunded }
public sealed class Order : Entity
{
    public Guid ShopperId { get; set; }
    public Guid QuoteId { get; set; }
    public string Number { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public OrderState State { get; set; } = OrderState.Open;
    public PaymentState PaymentState { get; set; } = PaymentState.Unpaid;
    public string Currency { get; set; } = "THB";
    public long SubtotalMinor { get; set; }
    public long ShippingMinor { get; set; }
    public long TotalMinor { get; set; }
    public string BuyerSnapshotJson { get; set; } = "{}";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}

public sealed class OrderLine : Entity
{
    public Guid OrderId { get; set; }
    public Guid VariantId { get; set; }
    public ProductKind Kind { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string VariantName { get; set; } = "";
    public int Quantity { get; set; }
    public long UnitPriceMinor { get; set; }
    public long TotalMinor { get; set; }
    public string SnapshotJson { get; set; } = "{}";
}

public enum ReservationState { Reserved, Committed, Released, Review }
public sealed class StockReservation : Entity
{
    public Guid OrderLineId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
    public ReservationState State { get; set; } = ReservationState.Reserved;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class StockMovement : Entity
{
    public Guid VariantId { get; set; }
    public Guid? OrderLineId { get; set; }
    public string EffectKey { get; set; } = "";
    public int OnHandDelta { get; set; }
    public int ReservedDelta { get; set; }
    public string Reason { get; set; } = "";
    public string Actor { get; set; } = "";
}

public enum AttemptState { AwaitingPayment, Expired, Replaced, Confirmed, Review }
public sealed class PaymentAttempt : Entity
{
    public Guid OrderId { get; set; }
    public string MerchantId { get; set; } = "";
    public string BillerNo { get; set; } = "";
    public string QrId { get; set; } = "";
    public string Ref1 { get; set; } = "";
    public string Ref2 { get; set; } = "";
    public string Payload { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string Profile { get; set; } = "";
    public long ExpectedMinor { get; set; }
    public AttemptState State { get; set; } = AttemptState.AwaitingPayment;
    public DateTimeOffset ExpiresAt { get; set; }
    public int VerificationCount { get; set; }
    public DateTimeOffset? LastVerificationAt { get; set; }
    public DateTimeOffset? VerificationLeaseUntil { get; set; }
    public Guid? VerificationLeaseToken { get; set; }
}

public sealed class BankTransaction : Entity
{
    public CommerceEnvironment Environment { get; set; }
    public string MerchantId { get; set; } = "";
    public string InstructionId { get; set; } = "";
    public string BillerNo { get; set; } = "";
    public string QrId { get; set; } = "";
    public string Ref1 { get; set; } = "";
    public string Ref2 { get; set; } = "";
    public long AmountMinor { get; set; }
    public string BankTransDate { get; set; } = "";
    public DateTimeOffset? PaymentOccurredAt { get; set; }
    public string PaymentTimeSource { get; set; } = "Unknown";
    public string ResultCode { get; set; } = "";
    public string? CrossBorderFlag { get; set; }
    public string? SwitchCode { get; set; }
    public string? CurrencyCode { get; set; }
    public string? SwitchFullName { get; set; }
    public string? PayerJson { get; set; }
    public bool Voided { get; set; }
}

public sealed class PaymentObservation : Entity
{
    public Guid? BankTransactionId { get; set; }
    public Guid? AttemptId { get; set; }
    public string Source { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public byte[] RawPayload { get; set; } = [];
    public string MetadataJson { get; set; } = "{}";
    public string Outcome { get; set; } = "";
}

public sealed class CallbackReceipt : Entity
{
    public CommerceEnvironment Environment { get; set; }
    public string MerchantId { get; set; } = "";
    public string InstructionId { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string BusinessHash { get; set; } = "";
    public string AckJson { get; set; } = "{}";
    public string AckCode { get; set; } = "";
    public Guid ObservationId { get; set; }
    public int DeliveryCount { get; set; } = 1;
    public DateTimeOffset LastReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PaymentAllocation : Entity
{
    public Guid BankTransactionId { get; set; }
    public Guid OrderId { get; set; }
    public long AmountMinor { get; set; }
    public bool Primary { get; set; } = true;
}

public sealed class VerificationRequest : Entity
{
    public Guid AttemptId { get; set; }
    public string Service { get; set; } = "";
    public string Actor { get; set; } = "";
    public string RequestUid { get; set; } = "";
    public string Outcome { get; set; } = "Pending";
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ReconciliationCase : Entity
{
    public long Version { get; set; } = 1;
    public Guid? OrderId { get; set; }
    public Guid? BankTransactionId { get; set; }
    public string EffectKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public string State { get; set; } = "Open";
    public string EvidenceJson { get; set; } = "{}";
    public string? ResolutionJson { get; set; }
    public string? Assignee { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class OutboxJob : Entity
{
    public string EffectKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public int Attempts { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class AuditEvent : Entity
{
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Resource { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string DataJson { get; set; } = "{}";
}

public sealed class NumberSequence : Entity
{
    public string Scope { get; set; } = "";
    public long Value { get; set; }
}

public sealed class MockCredit : Entity
{
    public Guid OrderId { get; set; }
    public Guid AttemptId { get; set; }
    public string RequestKey { get; set; } = "";
    public string Scenario { get; set; } = "";
    public string NoticeJson { get; set; } = "{}";
}

public enum FulfillmentState { Pending, Preparing, ReadyForPickup, Shipped, Delivered, Cancelled, Review }
public sealed class FulfillmentItem : Entity
{
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public ProductKind Kind { get; set; }
    public int Quantity { get; set; }
    public string DeliveryMethod { get; set; } = "";
    public FulfillmentState State { get; set; } = FulfillmentState.Pending;
    public long Version { get; set; } = 1;
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public string? OperatorNote { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public sealed class DigitalAsset : Entity
{
    public string Key { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
    public bool Ready { get; set; }
    public string UploadedBy { get; set; } = "";
}

public enum EntitlementState { Active, Revoked }
public sealed class Entitlement : Entity
{
    public Guid ShopperId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public int UnitIndex { get; set; }
    public DigitalDeliveryKind DigitalKind { get; set; }
    public string? DigitalAssetKey { get; set; }
    public string Name { get; set; } = "";
    public string VariantName { get; set; } = "";
    public EntitlementState State { get; set; } = EntitlementState.Active;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public DateTimeOffset? DownloadRequestedAt { get; set; }
    public int DownloadRequests { get; set; }
}

public sealed class DigitalDownloadEvent : Entity
{
    public Guid EntitlementId { get; set; }
    public Guid ShopperId { get; set; }
    public string RequestUid { get; set; } = "";
}

public sealed class EntitlementOperation : Entity
{
    public Guid EntitlementId { get; set; }
    public string OperationKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class PrivateStorageOptions
{
    public string RootPath { get; set; } = "";
    public long MaxAssetBytes { get; set; } = 104857600;
    public string GameServerKey { get; set; } = "";
    public string GameServerId { get; set; } = "mangos-ttb";
}
