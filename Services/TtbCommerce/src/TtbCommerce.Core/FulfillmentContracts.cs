namespace TtbCommerce.Core;

public sealed record BuyerDeliverySnapshot(int SchemaVersion, BuyerInput Buyer, DeliveryInput Delivery);
public sealed record FulfillmentActionInput(long ExpectedVersion, string Action, string? Carrier = null, string? TrackingNumber = null, string? Note = null);
public sealed record FulfillmentView(Guid Id, Guid OrderLineId, string Sku, string Name, string Kind, int Quantity, string DeliveryMethod, string State, long Version,
    string? Carrier, string? TrackingNumber, DateTimeOffset? ShippedAt, DateTimeOffset? DeliveredAt);
public sealed record FulfillmentQueueItem(Guid OrderId, string OrderNumber, string OrderState, BuyerInput Buyer, DeliveryInput Delivery, FulfillmentView Item);
public sealed record FulfillmentQueuePage(FulfillmentQueueItem[] Items, Guid? NextCursor);
