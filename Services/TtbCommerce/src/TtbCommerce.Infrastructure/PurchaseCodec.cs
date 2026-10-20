using System.Text.Json;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public static class PurchaseCodec
{
    public static PurchaseSnapshot Read(Quote quote)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<PurchaseSnapshot>(quote.SnapshotJson, CatalogService.Json);
            // PostgreSQL jsonb normalizes object order/whitespace. Hash the typed canonical representation, never jsonb's text formatting.
            if (snapshot is null || snapshot.SchemaVersion != 1 || SecretTokens.Hash(JsonSerializer.Serialize(snapshot, CatalogService.Json)) != quote.SnapshotHash ||
                snapshot.TotalMinor != quote.TotalMinor || snapshot.SubtotalMinor != quote.SubtotalMinor || snapshot.ShippingMinor != quote.ShippingMinor)
                throw new CommerceException(409, "quote_integrity", "The saved quote cannot be validated. Create a new quote.");
            return snapshot;
        }
        catch (JsonException) { throw new CommerceException(409, "quote_integrity", "The saved quote cannot be read. Create a new quote."); }
    }
}
