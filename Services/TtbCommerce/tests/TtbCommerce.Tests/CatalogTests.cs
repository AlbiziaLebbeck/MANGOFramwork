using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class CatalogTests
{
    [PgFact]
    public async Task Admin_catalog_pages_past_one_hundred_products_and_refreshes_an_older_selection_without_exposing_drafts_to_shoppers()
    {
        await using var database = await PgDatabase.CreateAsync(); Guid[] ids;
        await using (var db = database.Open())
        {
            var products = Enumerable.Range(0, 101).Select(i => new Product { Slug = "paged-" + i, Name = "Draft " + i }).ToArray();
            db.Products.AddRange(products); await db.SaveChangesAsync(); ids = products.Select(x => x.Id).ToArray();
        }
        await using var factory = new CommerceApiFactory(database.ConnectionString);
        using var admin = factory.ShopperClient("admin"); using var buyer = factory.ShopperClient("buyer");
        await CartTests.SignInAsync(admin); await CartTests.SignInAsync(buyer);
        var seen = new List<Guid>(); Guid? cursor = null;
        do
        {
            var page = await admin.GetFromJsonAsync<JsonElement[]>("/ttb-commerce/v1/admin/catalog?limit=40" + (cursor is null ? "" : "&before=" + cursor));
            Assert.NotNull(page); seen.AddRange(page.Select(x => x.GetProperty("product").GetProperty("id").GetGuid()));
            cursor = page.Length == 40 ? page[^1].GetProperty("product").GetProperty("id").GetGuid() : null;
        } while (cursor is not null);
        Assert.Equal(101, seen.Count); Assert.Equal(101, seen.Distinct().Count()); Assert.All(ids, id => Assert.Contains(id, seen));
        var selected = await admin.GetFromJsonAsync<JsonElement>("/ttb-commerce/v1/admin/catalog/" + seen[^1]); Assert.Equal(seen[^1], selected.GetProperty("product").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync("/ttb-commerce/v1/admin/catalog/" + seen[^1])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/ttb-commerce/v1/admin/catalog?limit=0")).StatusCode);
    }

    [PgFact]
    public async Task Concurrent_stock_adjustment_retries_apply_one_delta_and_cannot_reuse_a_key_for_changed_input()
    {
        await using var database = await PgDatabase.CreateAsync(); Guid variantId;
        await using (var db = database.Open()) variantId = (await PublishedAsync(db, "stock-idempotent", stock: 5)).Variant.Id;
        var input = new StockAdjustmentInput(3, "Counted returned stock", Guid.NewGuid().ToString("D"));
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); await new CatalogService(db).AdjustStockAsync(variantId, input, "test:admin", default); }));
        await using var check = database.Open(); Assert.Equal(8, (await check.Variants.SingleAsync()).OnHand);
        Assert.Equal(1, await check.StockMovements.CountAsync(x => x.EffectKey == "adjust:" + input.OperationId));
        Assert.Equal("operation_conflict", (await Assert.ThrowsAsync<CommerceException>(() => new CatalogService(check).AdjustStockAsync(variantId, input with { OnHandDelta = 9 }, "test:admin", default))).Code);
    }
    [PgFact]
    public async Task Model_binding_is_global_public_only_and_rejects_ambiguous_products()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var db = database.Open(); var catalog = new CatalogService(db);
        var first = await catalog.CreateAsync(Draft("model-first") with { Preview = new("shared-model") }, "test:admin", default);
        Assert.Equal(404, (await Assert.ThrowsAsync<CommerceException>(() => catalog.ByModelAsync("shared-model", default))).Status);
        await catalog.WriteVariantAsync(first.Id, null, new(first.Version, "MODEL-FIRST", "Standard", 1000, true, true, []), "test:admin", default);
        await catalog.PublishAsync(first.Id, new(first.Version, true), "test:admin", default);
        Assert.Equal(first.Id, (await catalog.ByModelAsync("shared-model", default)).Id);
        var second = await catalog.CreateAsync(Draft("model-second") with { Preview = new("shared-model") }, "test:admin", default);
        await catalog.WriteVariantAsync(second.Id, null, new(second.Version, "MODEL-SECOND", "Standard", 2000, true, true, []), "test:admin", default);
        await catalog.PublishAsync(second.Id, new(second.Version, true), "test:admin", default);
        Assert.Equal("model_binding_ambiguous", (await Assert.ThrowsAsync<CommerceException>(() => catalog.ByModelAsync("shared-model", default))).Code);
        Assert.Equal(first.Id, (await catalog.GetAsync(first.Id, default)).Id);
    }
    internal static ProductDraftInput Draft(string slug, ProductKind kind = ProductKind.Physical) =>
        new(slug, "โคมไฟ MANGO", "สินค้าในเมตาเวิร์ส", kind, "/Commerce/images/lamp.webp", new("lamp"));

    internal static async Task<(Product Product, ProductVariant Variant)> PublishedAsync(CommerceDb db, string slug = "mango-lamp", ProductKind kind = ProductKind.Physical, int stock = 5, long price = 19978)
    {
        var catalog = new CatalogService(db);
        var product = await catalog.CreateAsync(Draft(slug, kind), "test:admin", default);
        var variant = await catalog.WriteVariantAsync(product.Id, null, new(product.Version, slug.ToUpperInvariant(), "สีมะม่วง", price, true, kind == ProductKind.Physical, []), "test:admin", default);
        if (kind == ProductKind.Physical) await catalog.AdjustStockAsync(variant.Id, new(stock, "Opening stock", Guid.NewGuid().ToString("D")), "test:admin", default);
        await catalog.PublishAsync(product.Id, new(product.Version, true), "test:admin", default);
        return (product, variant);
    }

    [PgFact]
    public async Task Drafts_are_hidden_and_publication_requires_a_deliverable_priced_variant()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var db = database.Open();
        var catalog = new CatalogService(db);
        var draft = await catalog.CreateAsync(Draft("draft-lamp"), "test:admin", default);
        Assert.Empty((await catalog.ListAsync(24, null, default)).Items);
        Assert.Equal(404, (await Assert.ThrowsAsync<CommerceException>(() => catalog.GetAsync(draft.Id, default))).Status);
        await Assert.ThrowsAsync<CommerceException>(() => catalog.PublishAsync(draft.Id, new(draft.Version, true), "test:admin", default));
        var variant = await catalog.WriteVariantAsync(draft.Id, null, new(draft.Version, "DRAFT", "Standard", 0, true, true, []), "test:admin", default);
        await Assert.ThrowsAsync<CommerceException>(() => catalog.PublishAsync(draft.Id, new(draft.Version, true), "test:admin", default));
        await catalog.WriteVariantAsync(draft.Id, variant.Id, new(draft.Version, "DRAFT", "Standard", 1000, true, true, []), "test:admin", default);
        await catalog.PublishAsync(draft.Id, new(draft.Version, true), "test:admin", default);
        Assert.Single((await catalog.ListAsync(24, null, default)).Items);
    }

    [PgFact]
    public async Task Stale_editor_cannot_overwrite_new_product_data_and_stock_is_audited()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var db = database.Open();
        var catalog = new CatalogService(db);
        var (product, variant) = await PublishedAsync(db);
        var oldVersion = product.Version;
        await catalog.EditAsync(product.Id, new(oldVersion, "ชื่อใหม่", "รายละเอียด", "", new("lamp")), "test:admin", default);
        var conflict = await Assert.ThrowsAsync<CommerceException>(() => catalog.EditAsync(product.Id, new(oldVersion, "Stale", "", "", new("lamp")), "test:admin", default));
        Assert.Equal("catalog_changed", conflict.Code);
        await Assert.ThrowsAsync<CommerceException>(() => catalog.AdjustStockAsync(variant.Id, new(-6, "Too many", Guid.NewGuid().ToString("D")), "test:admin", default));
        Assert.Equal(5, (await catalog.GetAsync(product.Id, default)).Variants[0].AvailableQuantity);
        Assert.Equal(1, await db.StockMovements.CountAsync());
    }

    [PgFact]
    public async Task Digital_asset_keys_are_private_and_keyset_pagination_does_not_repeat_products()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var db = database.Open();
        var catalog = new CatalogService(db);
        var first = await PublishedAsync(db, "first-digital", ProductKind.Digital);
        db.DigitalAssets.Add(new DigitalAsset { Key = "paid_private_asset", FileName = "private.txt", ByteLength = 1, Sha256 = new string('0', 64), Ready = true, UploadedBy = "test" });
        await db.SaveChangesAsync();
        await catalog.WriteVariantAsync(first.Product.Id, first.Variant.Id,
            new(first.Product.Version, "FIRST-DIGITAL", "Download", 1000, true, false, [], DigitalDeliveryKind.Download, "paid_private_asset"), "test:admin", default);
        await PublishedAsync(db, "second-digital", ProductKind.Digital);
        var page1 = await catalog.ListAsync(1, null, default);
        Assert.Single(page1.Items);
        Assert.NotNull(page1.NextCursor);
        var page2 = await catalog.ListAsync(1, page1.NextCursor, default);
        Assert.Single(page2.Items);
        Assert.Null(page2.NextCursor);
        Assert.NotEqual(page1.Items[0].Id, page2.Items[0].Id);
        Assert.DoesNotContain("paid_private_asset", JsonSerializer.Serialize(page1));
        Assert.DoesNotContain("paid_private_asset", JsonSerializer.Serialize(await catalog.GetAsync(first.Product.Id, default)));
    }

    [PgFact]
    public async Task Catalog_rejects_executable_asset_urls_and_private_path_traversal()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var db = database.Open();
        var catalog = new CatalogService(db);
        await Assert.ThrowsAsync<CommerceException>(() => catalog.CreateAsync(Draft("bad-image") with { ThumbnailUrl = "javascript:alert(1)" }, "test:admin", default));
        await Assert.ThrowsAsync<CommerceException>(() => catalog.CreateAsync(Draft("bad-model") with { Preview = new("../private") }, "test:admin", default));
        var draft = await catalog.CreateAsync(Draft("download", ProductKind.Digital), "test:admin", default);
        await Assert.ThrowsAsync<CommerceException>(() => catalog.WriteVariantAsync(draft.Id, null, new(draft.Version, "DOWNLOAD", "Download", 1000, true, false, [], DigitalDeliveryKind.Download, "../../etc/passwd"), "test:admin", default));
    }

    [PgFact]
    public async Task Shopper_cannot_publish_products_but_configured_admin_can()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var factory = new CommerceApiFactory(database.ConnectionString);
        using var shopper = factory.ShopperClient("alice");
        using var admin = factory.ShopperClient("admin");
        static async Task SignIn(HttpClient client)
        {
            var result = await client.PostAsync("/ttb-commerce/v1/session", null);
            result.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Add("X-CSRF-Token", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString());
        }
        await SignIn(shopper); await SignIn(admin);
        var body = new { slug = "admin-created", name = "สินค้า", description = "รายละเอียด", kind = "physical", thumbnailUrl = "", preview = new { modelKey = "lamp" } };
        Assert.Equal(HttpStatusCode.Forbidden, (await shopper.PostAsJsonAsync("/ttb-commerce/v1/admin/products", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/ttb-commerce/v1/admin/products", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await shopper.GetAsync("/ttb-commerce/v1/admin/catalog")).StatusCode);
    }
}
