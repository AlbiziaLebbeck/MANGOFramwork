using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class LocalDemoSeeder(CommerceDb db, CommerceOptions options, CatalogService catalog)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        MockBankService.Guard(options);
        const string actor = "system:local-demo";
        var definitions = new[]
        {
            (Slug: "mango-orbit-lamp", Name: "Orbit Lamp", Kind: ProductKind.Physical, Price: 9900L, Model: "orbit-lamp"),
            (Slug: "mango-sculpture", Name: "Mango Sculpture", Kind: ProductKind.Physical, Price: 1000L, Model: "mango-sculpture"),
            (Slug: "mango-digital-star", Name: "Mango Star — Digital", Kind: ProductKind.Digital, Price: 100L, Model: "mango-star")
        };
        foreach (var definition in definitions)
        {
            if (await db.Products.AnyAsync(x => x.Slug == definition.Slug, cancellationToken)) continue;
            var product = await catalog.CreateAsync(new(definition.Slug, definition.Name,
                "สินค้าจำลองสำหรับทดสอบระบบ ไม่มีการรับเงินจริงหรือจัดส่งสินค้า", definition.Kind, "", new(definition.Model)), actor, cancellationToken);
            var physical = definition.Kind == ProductKind.Physical;
            var variant = await catalog.WriteVariantAsync(product.Id, null, new(product.Version, definition.Slug.ToUpperInvariant(), "Standard", definition.Price, true,
                physical, new() { ["สี"] = physical ? "Mango" : "Digital" }), actor, cancellationToken);
            if (physical) await catalog.AdjustStockAsync(variant.Id, new(20, "Opening local demo stock", Guid.NewGuid().ToString("D")), actor, cancellationToken);
            await catalog.PublishAsync(product.Id, new(product.Version, true), actor, cancellationToken);
        }
    }
}
