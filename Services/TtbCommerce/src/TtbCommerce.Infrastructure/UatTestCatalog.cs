using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed record UatTestCatalogResult(bool AlreadyPrepared, Guid[] ProductIds, int PhysicalProducts, int DigitalProducts);

// Explicit operator command only. This creates merchandise, never a payment, bank profile or grant.
public sealed class UatTestCatalog(CommerceDb db, CommerceOptions commerce, OperationsOptions operations, TimeProvider clock)
{
    public const string Actor = "system:uat-test-catalog";
    public const string Marker = "uat-test-catalog:v1";
    private sealed record Entry(string Slug, string Name, ProductKind Kind, long Price, string Model);
    private static readonly Entry[] Entries = [
        new("mango-orbit-lamp", "Orbit Lamp", ProductKind.Physical, 9900, "orbit-lamp"),
        new("mango-sculpture", "Mango Sculpture", ProductKind.Physical, 1000, "mango-sculpture"),
        new("mango-digital-star", "Mango Star — Digital", ProductKind.Digital, 100, "mango-star")
    ];
    private sealed record Preparation(Guid[] ProductIds, Guid[] VariantIds);

    public async Task<UatTestCatalogResult> PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (commerce.Environment != CommerceEnvironment.Uat || commerce.PaymentMode != PaymentMode.Bank)
            throw new CommerceException(409, "uat_catalog_environment", "Test merchandise preparation requires UAT in Bank mode.");
        if (operations.RecoveryFreeze) throw new CommerceException(503, "recovery_frozen", "The deployment is frozen for recovery inspection.");
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Catalog preparation must own its transaction.");
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({Marker}, 0))", cancellationToken);
        var identity = await db.Deployments.AsNoTracking().SingleOrDefaultAsync(x => x.Key == "singleton", cancellationToken);
        if (identity?.Environment != CommerceEnvironment.Uat)
            throw new CommerceException(409, "uat_catalog_database", "An existing UAT deployment marker is required; another database is never adopted.");
        var marker = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Actor == Actor && x.Action == "uat-test-catalog.prepare" && x.Resource == Marker, cancellationToken);
        if (marker is not null)
        {
            var prior = JsonSerializer.Deserialize<Preparation>(marker.DataJson, CatalogService.Json);
            if (prior is null || prior.ProductIds.Length != 3 || prior.VariantIds.Length != 3 ||
                await db.Products.CountAsync(x => prior.ProductIds.Contains(x.Id), cancellationToken) != 3 ||
                await db.Variants.CountAsync(x => prior.VariantIds.Contains(x.Id), cancellationToken) != 3)
                throw new CommerceException(409, "uat_catalog_incomplete", "A prepared test catalog was altered or removed; inspect it rather than recreating stock.");
            // Subsequent admin edits, publication, stock consumption and orders must survive every retry.
            return new(true, prior.ProductIds, 2, 1);
        }
        if (await db.Orders.AnyAsync(cancellationToken) || await db.Attempts.AnyAsync(cancellationToken) ||
            await db.BankTransactions.AnyAsync(cancellationToken) || await db.Observations.AnyAsync(cancellationToken) ||
            await db.CallbackReceipts.AnyAsync(cancellationToken) || await db.Voids.AnyAsync(cancellationToken) ||
            await db.Reports.AnyAsync(cancellationToken) || await db.ManualRefunds.AnyAsync(cancellationToken))
            throw new CommerceException(409, "uat_catalog_not_pristine", "Initial preparation is refused after financial activity; use normal catalog administration.");
        var slugs = Entries.Select(x => x.Slug).ToArray();
        var skus = Entries.Select(x => "UAT-" + x.Slug.ToUpperInvariant()).ToArray();
        var models = Entries.Select(x => x.Model).ToArray();
        if (await db.Products.AnyAsync(x => slugs.Contains(x.Slug), cancellationToken) || await db.Variants.AnyAsync(x => skus.Contains(x.Sku), cancellationToken) ||
            await db.Products.FromSqlInterpolated($"SELECT * FROM ttb.\"Products\" WHERE \"PreviewJson\" ->> 'modelKey' = ANY({models})").AnyAsync(cancellationToken))
            throw new CommerceException(409, "uat_catalog_collision", "A target slug, SKU or model binding already exists; no custom merchandise is overwritten.");
        var now = clock.GetUtcNow(); var productIds = new List<Guid>(); var variantIds = new List<Guid>();
        foreach (var entry in Entries)
        {
            var physical = entry.Kind == ProductKind.Physical;
            var product = new Product { Slug = entry.Slug, Name = entry.Name, Kind = entry.Kind, Published = true, TaxCode = "Unspecified",
                Description = "สินค้าจำลอง UAT สำหรับทดสอบระบบ ไม่มีการส่งมอบสินค้าจริง", PreviewJson = JsonSerializer.Serialize(new ProductPreview(entry.Model), CatalogService.Json), CreatedAt = now };
            var variant = new ProductVariant { ProductId = product.Id, Sku = "UAT-" + entry.Slug.ToUpperInvariant(), Name = "Standard", PriceMinor = entry.Price,
                TrackStock = physical, OnHand = physical ? 20 : 0, DigitalKind = DigitalDeliveryKind.InventoryItem,
                OptionsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["สี"] = physical ? "Mango" : "Digital" }, CatalogService.Json), CreatedAt = now };
            db.Products.Add(product); db.Variants.Add(variant); productIds.Add(product.Id); variantIds.Add(variant.Id);
            if (physical)
            {
                db.StockMovements.Add(new StockMovement { VariantId = variant.Id, EffectKey = Marker + ":" + entry.Slug, OnHandDelta = 20,
                    Reason = "Opening explicitly prepared UAT test stock; synthetic merchandise only", Actor = Actor, CreatedAt = now });
                db.AuditEvents.Add(new AuditEvent { Actor = Actor, Action = "stock.adjust", Resource = $"catalog:{variant.Id}",
                    DataJson = JsonSerializer.Serialize(new { onHandDelta = 20, fixture = Marker }), CreatedAt = now });
            }
            foreach (var action in new[] { "catalog.create", "catalog.publish" })
                db.AuditEvents.Add(new AuditEvent { Actor = Actor, Action = action, Resource = $"catalog:{product.Id}", CreatedAt = now });
        }
        db.AuditEvents.Add(new AuditEvent { Actor = Actor, Action = "uat-test-catalog.prepare", Resource = Marker,
            DataJson = JsonSerializer.Serialize(new Preparation(productIds.ToArray(), variantIds.ToArray()), CatalogService.Json), CreatedAt = now });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(false, productIds.ToArray(), 2, 1);
    }
}
