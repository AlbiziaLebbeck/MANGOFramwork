namespace TtbCommerce.Core;

public sealed record ProductPreview(string ModelKey, double Scale = 1, double InitialRotation = 0, double MinZoom = 0.5, double MaxZoom = 3);
public sealed record ProductDraftInput(string Slug, string Name, string Description, ProductKind Kind, string ThumbnailUrl, ProductPreview Preview, string TaxCode = "Unspecified");
public sealed record ProductEditInput(long ExpectedVersion, string Name, string Description, string ThumbnailUrl, ProductPreview Preview, string TaxCode = "Unspecified");
public sealed record VariantInput(long ExpectedProductVersion, string Sku, string Name, long PriceMinor, bool Active, bool TrackStock,
    Dictionary<string, string> Options, DigitalDeliveryKind DigitalKind = DigitalDeliveryKind.InventoryItem, string? DigitalAssetKey = null);
public sealed record PublicationInput(long ExpectedVersion, bool Published);
public sealed record StockAdjustmentInput(int OnHandDelta, string Reason, string OperationId);
public sealed record VariantView(Guid Id, string Sku, string Name, long PriceMinor, bool Active, int? AvailableQuantity, Dictionary<string, string> Options);
public sealed record ProductView(Guid Id, string Slug, string Name, string Description, string Kind, long Version, string ThumbnailUrl, ProductPreview Preview, VariantView[] Variants);
public sealed record ProductPage(ProductView[] Items, Guid? NextCursor);
