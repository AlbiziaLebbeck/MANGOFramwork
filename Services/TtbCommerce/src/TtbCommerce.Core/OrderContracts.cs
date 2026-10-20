namespace TtbCommerce.Core;

public sealed record CreateOrderInput(Guid QuoteId);
public sealed record OrderLineView(Guid Id, Guid ProductId, Guid VariantId, string Sku, string Name, string VariantName, string Kind,
    int Quantity, long UnitPriceMinor, long TotalMinor, string ThumbnailUrl);
public sealed record OrderView(Guid Id, string Number, string State, string PaymentState, string Currency, long SubtotalMinor, long ShippingMinor, long TotalMinor,
    OrderLineView[] Lines, BuyerInput Buyer, DeliveryInput Delivery, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? ConfirmedAt, bool CanCancel);
public sealed record OrderPage(OrderView[] Items, Guid? NextCursor);
