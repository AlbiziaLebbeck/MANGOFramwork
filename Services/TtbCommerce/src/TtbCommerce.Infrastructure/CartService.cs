using System.Data;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class CartService(CommerceDb db, CommerceOptions options, TimeProvider clock)
{
    private sealed record CartProduct(CartLine Line, Product Product, ProductVariant Variant);

    public async Task<CartView> GetAsync(Guid shopperId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var cart = await db.Carts.AsNoTracking().SingleOrDefaultAsync(x => x.ShopperId == shopperId, cancellationToken);
        var result = cart is null ? new CartView(0, [], 0) : View(cart.Version, await LinesAsync(cart.Id, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CartView> ChangeAsync(Guid shopperId, Guid variantId, CartChange input, CancellationToken cancellationToken)
    {
        if (input.Quantity is < 0 or > 99 || input.ExpectedVersion < 0) throw Invalid("Quantity must be0–99 with a valid cart version.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var cart = await LockAsync(shopperId, cancellationToken);
        CheckVersion(cart, input.ExpectedVersion);
        var line = await db.CartLines.SingleOrDefaultAsync(x => x.CartId == cart.Id && x.VariantId == variantId, cancellationToken);
        if (input.Quantity > 0)
        {
            var variant = await db.Variants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == variantId && x.Active, cancellationToken);
            if (variant is null || !await db.Products.AnyAsync(x => x.Id == variant.ProductId && x.Published, cancellationToken)) throw Unavailable();
            if (variant.TrackStock && variant.OnHand - variant.Reserved < input.Quantity) throw new CommerceException(409, "insufficient_stock", "The requested quantity is not available.");
            if (line is null && await db.CartLines.CountAsync(x => x.CartId == cart.Id, cancellationToken) >= options.MaxCartLines) throw Invalid("The cart has reached its item limit.");
            if (line is null) { line = new() { CartId = cart.Id, VariantId = variantId }; db.CartLines.Add(line); }
            line.Quantity = input.Quantity;
        }
        else if (line is not null) db.CartLines.Remove(line);
        cart.Version++; cart.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        var view = View(cart.Version, await LinesAsync(cart.Id, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return view;
    }

    public async Task<QuoteView> QuoteAsync(ShopperActor actor, QuoteInput input, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var cart = await LockAsync(actor.ShopperId, cancellationToken);
        CheckVersion(cart, input.CartVersion);
        var lines = await LinesAsync(cart.Id, cancellationToken);
        if (lines.Length == 0) throw new CommerceException(409, "cart_empty", "Add a product before checkout.");
        if (lines.Any(x => !x.Product.Published || !x.Variant.Active || x.Variant.PriceMinor <= 0)) throw Unavailable();
        if (lines.Any(x => x.Variant.TrackStock && x.Variant.OnHand - x.Variant.Reserved < x.Line.Quantity))
            throw new CommerceException(409, "insufficient_stock", "Some items are no longer available in the requested quantity.");
        var physical = lines.Any(x => x.Product.Kind == ProductKind.Physical);
        var buyer = NormalizeBuyer(input.Buyer, actor.DisplayName, physical);
        var delivery = NormalizeDelivery(input.Delivery, physical, options);
        var purchaseLines = lines.Select(x => new PurchaseLine(x.Product.Id, x.Variant.Id, x.Product.Version, x.Product.Kind, x.Variant.DigitalKind,
            x.Variant.Sku, x.Product.Name, x.Variant.Name, x.Line.Quantity, x.Variant.PriceMinor, checked(x.Variant.PriceMinor * x.Line.Quantity), x.Variant.TrackStock,
            x.Product.TaxCode, x.Variant.OptionsJson, x.Product.PreviewJson, x.Product.ThumbnailUrl, x.Variant.DigitalAssetKey)).ToArray();
        var subtotal = purchaseLines.Aggregate(0L, (sum, x) => checked(sum + x.TotalMinor));
        var shipping = delivery.Method == "shipping" ? options.ShippingFeeMinor : 0;
        var total = checked(subtotal + shipping);
        if (total <= 0 || total > options.MaxOrderMinor) throw Invalid("The cart total exceeds the supported payment limit.");
        var snapshot = new PurchaseSnapshot(1, buyer, delivery, DeliveryPolicyHash(options), purchaseLines, subtotal, shipping, total);
        var json = JsonSerializer.Serialize(snapshot, CatalogService.Json);
        var quote = new Quote { ShopperId = actor.ShopperId, CartId = cart.Id, CartVersion = cart.Version, SubtotalMinor = subtotal, ShippingMinor = shipping,
            TotalMinor = total, SnapshotJson = json, SnapshotHash = SecretTokens.Hash(json), ExpiresAt = clock.GetUtcNow().AddMinutes(options.QuoteMinutes) };
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(quote.Id, quote.CartVersion, View(cart.Version, lines).Lines, buyer, delivery, subtotal, shipping, total, "THB", quote.ExpiresAt);
    }

    internal async Task<Cart> LockAsync(Guid shopperId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Cart locking requires a transaction.");
        var key = $"cart:{shopperId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var cart = (await db.Carts.FromSqlInterpolated($"SELECT * FROM ttb.\"Carts\" WHERE \"ShopperId\" = {shopperId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();
        if (cart is not null) return cart;
        cart = new() { ShopperId = shopperId, Version = 0 };
        db.Carts.Add(cart);
        await db.SaveChangesAsync(cancellationToken);
        return cart;
    }

    private Task<CartProduct[]> LinesAsync(Guid cartId, CancellationToken cancellationToken) =>
        (from line in db.CartLines.AsNoTracking()
         join variant in db.Variants.AsNoTracking() on line.VariantId equals variant.Id
         join product in db.Products.AsNoTracking() on variant.ProductId equals product.Id
         where line.CartId == cartId
         orderby line.VariantId
         select new CartProduct(line, product, variant)).ToArrayAsync(cancellationToken);

    private static CartView View(long version, CartProduct[] lines)
    {
        var result = lines.Select(x => new CartLineView(x.Variant.Id, x.Product.Id, x.Variant.Sku, x.Product.Name, x.Variant.Name, x.Product.Kind.ToString(), x.Product.ThumbnailUrl,
            x.Line.Quantity, x.Variant.PriceMinor, checked(x.Variant.PriceMinor * x.Line.Quantity), x.Product.Published && x.Variant.Active,
            x.Variant.TrackStock ? x.Variant.OnHand - x.Variant.Reserved : null)).ToArray();
        return new(version, result, result.Aggregate(0L, (sum, x) => checked(sum + x.TotalMinor)));
    }

    internal static string DeliveryPolicyHash(CommerceOptions options) => SecretTokens.Hash(JsonSerializer.Serialize(new { options.PickupEnabled, options.ShippingEnabled, options.ShippingFeeMinor }));

    private static BuyerInput NormalizeBuyer(BuyerInput buyer, string fallbackName, bool physical)
    {
        if (buyer is null) throw Invalid("Buyer details are required.");
        var name = string.IsNullOrWhiteSpace(buyer.Name) ? fallbackName : buyer.Name.Trim();
        var email = buyer.Email?.Trim();
        var phone = Regex.Replace(buyer.Phone ?? "", "[\\s()-]", "", RegexOptions.CultureInvariant);
        if (name.Length is < 1 or > 200 || email?.Length > 200 || !string.IsNullOrEmpty(email) && (!MailAddress.TryCreate(email, out var address) || address.Address != email) ||
            phone.Length > 0 && !Regex.IsMatch(phone, "^\\+?[0-9]{8,20}$", RegexOptions.CultureInvariant) || physical && phone.Length == 0)
            throw Invalid("Provide a valid buyer name, email and phone (phone is required for physical goods).");
        return new(name, string.IsNullOrEmpty(email) ? null : email, phone.Length == 0 ? null : phone);
    }

    private static DeliveryInput NormalizeDelivery(DeliveryInput delivery, bool physical, CommerceOptions options)
    {
        if (!physical) return new("digital");
        if (delivery is null) throw Invalid("Choose pickup or shipping.");
        if (delivery.Method == "pickup" && options.PickupEnabled) return new("pickup");
        if (delivery.Method != "shipping" || !options.ShippingEnabled || delivery.Address is null) throw Invalid("That delivery method is unavailable.");
        var address = delivery.Address;
        var text = new[] { address.Line1, address.Subdistrict, address.District, address.Province };
        if (text.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200) || address.Country != "TH" || !Regex.IsMatch(address.PostalCode ?? "", "^[0-9]{5}$", RegexOptions.CultureInvariant))
            throw Invalid("Provide a complete Thailand shipping address.");
        return delivery;
    }

    internal static void CheckVersion(Cart cart, long expected)
    {
        if (cart.Version != expected) throw new CommerceException(409, "cart_changed", "The cart changed. Reload it before continuing.");
    }
    private static CommerceException Invalid(string message) => new(400, "checkout_invalid", message);
    private static CommerceException Unavailable() => new(409, "product_unavailable", "Some products are no longer available. Review your cart.");
}
