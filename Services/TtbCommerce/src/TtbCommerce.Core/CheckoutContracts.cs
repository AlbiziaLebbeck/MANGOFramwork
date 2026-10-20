namespace TtbCommerce.Core;

public sealed record CartChange(int Quantity, long ExpectedVersion);
public sealed record CartLineView(Guid VariantId, Guid ProductId, string Sku, string Name, string VariantName, string Kind, string ThumbnailUrl,
    int Quantity, long UnitPriceMinor, long TotalMinor, bool Available, int? AvailableQuantity);
public sealed record CartView(long Version, CartLineView[] Lines, long SubtotalMinor);
public sealed record BuyerInput(string Name, string? Email = null, string? Phone = null);
public sealed record ShippingAddress(string Line1, string Subdistrict, string District, string Province, string PostalCode, string Country = "TH");
public sealed record DeliveryInput(string Method, ShippingAddress? Address = null);
public sealed record QuoteInput(long CartVersion, BuyerInput Buyer, DeliveryInput Delivery);

// This internal purchase snapshot may include private asset references. Never serialize it directly in a shopper response.
public sealed record PurchaseLine(Guid ProductId, Guid VariantId, long ProductVersion, ProductKind Kind, DigitalDeliveryKind DigitalKind,
    string Sku, string Name, string VariantName, int Quantity, long UnitPriceMinor, long TotalMinor, bool TrackStock, string TaxCode,
    string OptionsJson, string PreviewJson, string ThumbnailUrl, string? DigitalAssetKey);
public sealed record PurchaseSnapshot(int SchemaVersion, BuyerInput Buyer, DeliveryInput Delivery, string DeliveryPolicyHash, PurchaseLine[] Lines,
    long SubtotalMinor, long ShippingMinor, long TotalMinor);
public sealed record QuoteView(Guid Id, long CartVersion, CartLineView[] Lines, BuyerInput Buyer, DeliveryInput Delivery,
    long SubtotalMinor, long ShippingMinor, long TotalMinor, string Currency, DateTimeOffset ExpiresAt);
