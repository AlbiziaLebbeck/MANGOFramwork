using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class CatalogService(CommerceDb db)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductPage> ListAsync(int limit, Guid? cursor, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw Invalid("Page size must be between1 and100.");
        var query = db.Products.AsNoTracking().Where(x => x.Published);
        if (cursor is not null) query = query.Where(x => x.Id.CompareTo(cursor.Value) > 0);
        var products = await query.OrderBy(x => x.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        var page = products.Take(limit).ToArray();
        var ids = page.Select(x => x.Id).ToArray();
        var variants = await db.Variants.AsNoTracking().Where(x => ids.Contains(x.ProductId) && x.Active).OrderBy(x => x.Sku).ToArrayAsync(cancellationToken);
        return new(page.Select(x => View(x, variants.Where(v => v.ProductId == x.Id))).ToArray(), products.Length > limit ? page[^1].Id : null);
    }

    public async Task<ProductView> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Published, cancellationToken) ?? throw Missing();
        return View(product, await db.Variants.AsNoTracking().Where(x => x.ProductId == id && x.Active).OrderBy(x => x.Sku).ToArrayAsync(cancellationToken));
    }

    public async Task<ProductView> ByModelAsync(string modelKey, CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(modelKey, "^[A-Za-z0-9_-]{1,100}$", RegexOptions.CultureInvariant)) throw Missing();
        var matches = await db.Products.FromSqlInterpolated($"SELECT * FROM ttb.\"Products\" WHERE \"Published\" = TRUE AND \"PreviewJson\" ->> 'modelKey' = {modelKey} ORDER BY \"Id\" LIMIT 2")
            .AsNoTracking().ToArrayAsync(cancellationToken);
        if (matches.Length > 1) throw new CommerceException(409, "model_binding_ambiguous", "This model is used by multiple products. Configure its explicit product ID.");
        return matches.Length == 1 ? await GetAsync(matches[0].Id, cancellationToken) : throw Missing();
    }

    public async Task<object> AdminListAsync(CancellationToken cancellationToken, int limit = 100, Guid? before = null)
    {
        if (limit is < 1 or > 100) throw Invalid("Page size must be1–100.");
        var query = db.Products.AsNoTracking().AsQueryable();
        if (before is not null) query = query.Where(x => x.Id.CompareTo(before.Value) < 0);
        var products = await query.OrderByDescending(x => x.Id).Take(limit).ToArrayAsync(cancellationToken);
        var ids = products.Select(x => x.Id).ToArray();
        var variants = await db.Variants.AsNoTracking().Where(x => ids.Contains(x.ProductId)).OrderBy(x => x.Sku).ToArrayAsync(cancellationToken);
        return products.Select(p => new { product = p, variants = variants.Where(v => v.ProductId == p.Id).ToArray() }).ToArray();
    }

    public async Task<object> AdminGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new CommerceException(404, "not_found", "The product was not found.");
        var variants = await db.Variants.AsNoTracking().Where(x => x.ProductId == id).OrderBy(x => x.Sku).ToArrayAsync(cancellationToken);
        return new { product, variants };
    }

    public async Task<Product> CreateAsync(ProductDraftInput input, string actor, CancellationToken cancellationToken)
    {
        ValidateProduct(input.Name, input.Description, input.ThumbnailUrl, input.Preview, input.TaxCode);
        if (!Enum.IsDefined(input.Kind) || input.Slug is null || !Regex.IsMatch(input.Slug, "^[a-z0-9][a-z0-9-]{1,79}$", RegexOptions.CultureInvariant)) throw Invalid("Use a short lowercase product slug and a valid product kind.");
        var product = new Product { Slug = input.Slug, Name = input.Name.Trim(), Description = input.Description, Kind = input.Kind, ThumbnailUrl = input.ThumbnailUrl,
            PreviewJson = JsonSerializer.Serialize(input.Preview, Json), TaxCode = input.TaxCode };
        db.Products.Add(product);
        Audit(actor, "catalog.create", product.Id);
        await SaveAsync(cancellationToken);
        return product;
    }

    public async Task<Product> EditAsync(Guid id, ProductEditInput input, string actor, CancellationToken cancellationToken)
    {
        ValidateProduct(input.Name, input.Description, input.ThumbnailUrl, input.Preview, input.TaxCode);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var product = await LockProductAsync(id, input.ExpectedVersion, cancellationToken);
        product.Name = input.Name.Trim(); product.Description = input.Description; product.ThumbnailUrl = input.ThumbnailUrl;
        product.PreviewJson = JsonSerializer.Serialize(input.Preview, Json); product.TaxCode = input.TaxCode; product.Version++;
        Audit(actor, "catalog.edit", id);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return product;
    }

    public async Task<ProductVariant> WriteVariantAsync(Guid productId, Guid? variantId, VariantInput input, string actor, CancellationToken cancellationToken)
    {
        if (input.Sku is null || !Regex.IsMatch(input.Sku, "^[A-Z0-9][A-Z0-9_-]{0,63}$", RegexOptions.CultureInvariant) || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 ||
            input.PriceMinor is < 0 or > 999999999999 || input.Options is null || input.Options.Count > 10 || input.Options.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 50 || x.Value is null || x.Value.Length > 100) || !Enum.IsDefined(input.DigitalKind))
            throw Invalid("Variant SKU, name, price or options are invalid.");
        if (input.DigitalAssetKey is not null && !Regex.IsMatch(input.DigitalAssetKey, "^[a-zA-Z0-9_-]{1,100}$", RegexOptions.CultureInvariant)) throw Invalid("Use an opaque private asset key.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var product = await LockProductAsync(productId, input.ExpectedProductVersion, cancellationToken);
        ProductVariant variant;
        if (variantId is null) { variant = new() { ProductId = productId }; db.Variants.Add(variant); }
        else variant = (await db.Variants.FromSqlInterpolated($"SELECT * FROM ttb.\"Variants\" WHERE \"Id\" = {variantId.Value} AND \"ProductId\" = {productId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault() ?? throw Missing();
        if (variant.Reserved > 0 && variant.TrackStock != input.TrackStock) throw new CommerceException(409, "stock_reserved", "Stock tracking cannot change while stock is reserved.");
        if (product.Published && variantId is not null && !input.Active &&
            !await db.Variants.AnyAsync(x => x.ProductId == productId && x.Id != variantId && x.Active, cancellationToken))
            throw Invalid("Unpublish the product before disabling its last active variant.");
        if (product.Kind == ProductKind.Physical && input.DigitalAssetKey is not null) throw Invalid("Physical variants cannot expose a digital asset key.");
        if (product.Kind == ProductKind.Digital && input.DigitalKind == DigitalDeliveryKind.Download && input.DigitalAssetKey is not null &&
            !await db.DigitalAssets.AnyAsync(x => x.Key == input.DigitalAssetKey && x.Ready, cancellationToken)) throw Invalid("Upload the private downloadable asset before assigning its key.");
        if (product.Published && input.Active && (input.PriceMinor == 0 || product.Kind == ProductKind.Physical && !input.TrackStock ||
            product.Kind == ProductKind.Digital && input.DigitalKind == DigitalDeliveryKind.Download && input.DigitalAssetKey is null))
            throw Invalid("Published variants need a positive price and a valid delivery/stock configuration.");
        variant.Sku = input.Sku; variant.Name = input.Name.Trim(); variant.PriceMinor = input.PriceMinor; variant.Active = input.Active;
        variant.TrackStock = input.TrackStock; variant.OptionsJson = JsonSerializer.Serialize(input.Options, Json);
        variant.DigitalKind = input.DigitalKind; variant.DigitalAssetKey = input.DigitalAssetKey;
        product.Version++;
        Audit(actor, variantId is null ? "catalog.variant.create" : "catalog.variant.edit", variant.Id);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return variant;
    }

    public async Task<Product> PublishAsync(Guid id, PublicationInput input, string actor, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var product = await LockProductAsync(id, input.ExpectedVersion, cancellationToken);
        if (input.Published)
        {
            var variants = await db.Variants.Where(x => x.ProductId == id && x.Active).ToArrayAsync(cancellationToken);
            if (variants.Length == 0 || variants.Any(x => x.PriceMinor <= 0 || product.Kind == ProductKind.Physical && !x.TrackStock ||
                product.Kind == ProductKind.Digital && x.DigitalKind == DigitalDeliveryKind.Download && x.DigitalAssetKey is null))
                throw Invalid("Add active priced variants and configure delivery/stock before publishing.");
            var assetKeys = variants.Where(x => x.DigitalKind == DigitalDeliveryKind.Download && x.DigitalAssetKey != null).Select(x => x.DigitalAssetKey!).Distinct().ToArray();
            if (await db.DigitalAssets.CountAsync(x => assetKeys.Contains(x.Key) && x.Ready, cancellationToken) != assetKeys.Length)
                throw Invalid("A private download asset is unavailable. Repair it before publishing.");
        }
        product.Published = input.Published; product.Version++;
        Audit(actor, input.Published ? "catalog.publish" : "catalog.unpublish", id);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return product;
    }

    public async Task<ProductVariant> AdjustStockAsync(Guid id, StockAdjustmentInput input, string actor, CancellationToken cancellationToken)
    {
        if (input.OnHandDelta == 0 || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500 || input.Reason.Any(char.IsControl) || !Guid.TryParseExact(input.OperationId, "D", out var operationId)) throw Invalid("A stable UUID, stock delta and reason are required.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = operationId.ToString("D"); var operationLock = "stock-adjustment:" + key;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({operationLock}, 0))", cancellationToken);
        var effectKey = "adjust:" + key;
        var prior = await db.StockMovements.SingleOrDefaultAsync(x => x.EffectKey == effectKey, cancellationToken);
        if (prior is not null)
        {
            if (prior.VariantId != id || prior.OnHandDelta != input.OnHandDelta || prior.Reason != input.Reason || prior.Actor != actor) throw new CommerceException(409, "operation_conflict", "This operation ID already refers to another adjustment.");
            return await db.Variants.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
        }
        var variant = (await db.Variants.FromSqlInterpolated($"SELECT * FROM ttb.\"Variants\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault() ?? throw Missing();
        await db.Entry(variant).ReloadAsync(cancellationToken);
        var next = (long)variant.OnHand + input.OnHandDelta;
        if (!variant.TrackStock || next < variant.Reserved || next > int.MaxValue) throw new CommerceException(409, "stock_conflict", "The adjustment conflicts with stock tracking or reserved stock.");
        variant.OnHand = (int)next;
        db.StockMovements.Add(new StockMovement { VariantId = id, EffectKey = $"adjust:{key}", OnHandDelta = input.OnHandDelta, Reason = input.Reason, Actor = actor });
        Audit(actor, "inventory.adjust", id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return variant;
    }

    private async Task<Product> LockProductAsync(Guid id, long version, CancellationToken cancellationToken)
    {
        var product = (await db.Products.FromSqlInterpolated($"SELECT * FROM ttb.\"Products\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault() ?? throw Missing();
        if (product.Version != version) throw new CommerceException(409, "catalog_changed", "The product changed. Reload it and try again.");
        return product;
    }

    private static ProductView View(Product p, IEnumerable<ProductVariant> variants) => new(p.Id, p.Slug, p.Name, p.Description, p.Kind.ToString(), p.Version,
        p.ThumbnailUrl, JsonSerializer.Deserialize<ProductPreview>(p.PreviewJson, Json)!, variants.Select(v => new VariantView(v.Id, v.Sku, v.Name, v.PriceMinor, v.Active,
            v.TrackStock ? v.OnHand - v.Reserved : null, JsonSerializer.Deserialize<Dictionary<string, string>>(v.OptionsJson, Json)!)).ToArray());

    private void Audit(string actor, string action, Guid id) => db.AuditEvents.Add(new AuditEvent { Actor = actor, Action = action, Resource = $"catalog:{id}" });
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new CommerceException(409, "catalog_duplicate", "That product slug or SKU already exists."); }
    }

    private static void ValidateProduct(string name, string description, string thumbnail, ProductPreview preview, string taxCode)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || description is null || description.Length > 10000 || !SafeAssetUrl(thumbnail) ||
            string.IsNullOrWhiteSpace(taxCode) || taxCode.Length > 50 || preview is null || !Regex.IsMatch(preview.ModelKey ?? "", "^[a-zA-Z0-9_-]{1,100}$", RegexOptions.CultureInvariant) ||
            !double.IsFinite(preview.Scale) || preview.Scale is < 0.001 or > 100 || !double.IsFinite(preview.InitialRotation) ||
            !double.IsFinite(preview.MinZoom) || !double.IsFinite(preview.MaxZoom) || preview.MinZoom is < 0.1 or > 10 || preview.MaxZoom < preview.MinZoom || preview.MaxZoom > 100)
            throw Invalid("Product details, public image URL or preview settings are invalid.");
    }

    private static bool SafeAssetUrl(string value) => value is not null && value.Length <= 500 &&
        (value.Length == 0 || value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\') && !value.Contains("..", StringComparison.Ordinal) ||
         Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo));
    private static CommerceException Invalid(string message) => new(400, "catalog_invalid", message);
    private static CommerceException Missing() => new(404, "not_found", "The product or variant was not found.");
}
