using Microsoft.EntityFrameworkCore;
using Npgsql;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class UatTestCatalogTests
{
    private static UatTestCatalog Service(CommerceDb db, bool frozen = false) => new(db,
        new() { Environment = CommerceEnvironment.Uat, PaymentMode = PaymentMode.Bank }, new() { RecoveryFreeze = frozen }, TimeProvider.System);
    private static async Task<PgDatabase> DatabaseAsync()
    {
        var database = await PgDatabase.CreateAsync(); await using var db = database.Open();
        await DbGuard.AssertEnvironmentAsync(db, CommerceEnvironment.Uat); return database;
    }

    [Theory]
    [InlineData(CommerceEnvironment.Local, PaymentMode.Mock)]
    [InlineData(CommerceEnvironment.Local, PaymentMode.Bank)]
    [InlineData(CommerceEnvironment.Uat, PaymentMode.Mock)]
    [InlineData(CommerceEnvironment.Production, PaymentMode.Bank)]
    public async Task Other_environments_and_simulator_mode_are_rejected_before_any_connection(CommerceEnvironment environment, PaymentMode mode)
    {
        await using var db = new CommerceDb(new DbContextOptionsBuilder<CommerceDb>().UseNpgsql("Host=127.0.0.1;Port=1;Database=never_connect;Username=never_connect;Timeout=1").Options);
        var error = await Assert.ThrowsAsync<CommerceException>(() => new UatTestCatalog(db, new() { Environment = environment, PaymentMode = mode }, new(), TimeProvider.System).PrepareAsync());
        Assert.Equal("uat_catalog_environment", error.Code);
        Assert.Equal("recovery_frozen", (await Assert.ThrowsAsync<CommerceException>(() => Service(db, true).PrepareAsync())).Code);
    }

    [PgFact]
    public async Task Concurrent_preparation_creates_one_atomic_catalog_with_stock_audit_but_no_financial_or_mock_effect()
    {
        await using var database = await DatabaseAsync();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); return await Service(db).PrepareAsync(); }));
        Assert.Single(outcomes, x => !x.AlreadyPrepared); Assert.Equal(5, outcomes.Count(x => x.AlreadyPrepared));
        Assert.All(outcomes, x => Assert.Equal(outcomes[0].ProductIds, x.ProductIds));
        await using var check = database.Open(); Assert.Equal(3, await check.Products.CountAsync()); Assert.Equal(3, await check.Variants.CountAsync());
        Assert.All(await check.Products.ToArrayAsync(), product => { Assert.True(product.Published); Assert.Contains("จำลอง UAT", product.Description); });
        var catalog = new CatalogService(check);
        Assert.Equal(9900, (await catalog.ByModelAsync("orbit-lamp", default)).Variants.Single().PriceMinor);
        Assert.Equal(1000, (await catalog.ByModelAsync("mango-sculpture", default)).Variants.Single().PriceMinor);
        Assert.Equal(100, (await catalog.ByModelAsync("mango-star", default)).Variants.Single().PriceMinor);
        Assert.Equal(2, await check.Variants.CountAsync(x => x.TrackStock && x.OnHand == 20 && x.Reserved == 0));
        Assert.Equal(1, await check.Variants.CountAsync(x => !x.TrackStock && x.DigitalKind == DigitalDeliveryKind.InventoryItem && x.DigitalAssetKey == null));
        Assert.Equal(2, await check.StockMovements.CountAsync()); Assert.Equal(9, await check.AuditEvents.CountAsync());
        Assert.Equal(0, await check.Orders.CountAsync()); Assert.Equal(0, await check.Attempts.CountAsync()); Assert.Equal(0, await check.BankTransactions.CountAsync());
        Assert.Equal(0, await check.Allocations.CountAsync()); Assert.Equal(0, await check.Observations.CountAsync()); Assert.Equal(0, await check.CallbackReceipts.CountAsync());
        Assert.Equal(0, await check.Documents.CountAsync()); Assert.Equal(0, await check.Fulfillments.CountAsync()); Assert.Equal(0, await check.Entitlements.CountAsync());
        Assert.Equal(0, await check.MockCredits.CountAsync()); Assert.Equal(0, await check.MockVoids.CountAsync()); Assert.Equal(0, await check.Jobs.CountAsync());
    }

    [PgFact]
    public async Task Retry_preserves_admin_edits_unpublication_stock_consumption_and_later_orders()
    {
        await using var database = await DatabaseAsync(); await using var db = database.Open(); await Service(db).PrepareAsync();
        var product = await db.Products.SingleAsync(x => x.Slug == "mango-orbit-lamp"); var variant = await db.Variants.SingleAsync(x => x.ProductId == product.Id);
        await db.Products.Where(x => x.Id == product.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Edited test lamp").SetProperty(x => x.Published, false));
        await db.Variants.Where(x => x.Id == variant.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PriceMinor, 777L).SetProperty(x => x.OnHand, 3));
        await PaymentFixture.CreateAsync(database); // Later synthetic order proves a retry is a read-only no-op.
        var audits = await db.AuditEvents.CountAsync(); var stock = await db.StockMovements.CountAsync();
        var result = await Service(db).PrepareAsync(); Assert.True(result.AlreadyPrepared);
        Assert.Equal("Edited test lamp", (await db.Products.AsNoTracking().SingleAsync(x => x.Id == product.Id)).Name);
        Assert.False((await db.Products.AsNoTracking().SingleAsync(x => x.Id == product.Id)).Published);
        var current = await db.Variants.AsNoTracking().SingleAsync(x => x.Id == variant.Id); Assert.Equal(777, current.PriceMinor); Assert.Equal(3, current.OnHand);
        Assert.Equal(audits, await db.AuditEvents.CountAsync()); Assert.Equal(stock, await db.StockMovements.CountAsync()); Assert.Equal(1, await db.Orders.CountAsync());
    }

    [PgFact]
    public async Task A_database_failure_rolls_back_every_product_stock_row_and_marker_before_a_later_explicit_retry()
    {
        await using var database = await DatabaseAsync(); await using var db = database.Open();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION ttb.reject_test_catalog() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."Sku" = 'UAT-MANGO-SCULPTURE' THEN RAISE EXCEPTION 'Synthetic catalog failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_test_catalog BEFORE INSERT ON ttb."Variants" FOR EACH ROW EXECUTE FUNCTION ttb.reject_test_catalog();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).PrepareAsync()); db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Products.CountAsync()); Assert.Equal(0, await db.Variants.CountAsync()); Assert.Equal(0, await db.StockMovements.CountAsync()); Assert.Equal(0, await db.AuditEvents.CountAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_test_catalog ON ttb.\"Variants\"; DROP FUNCTION ttb.reject_test_catalog();");
        Assert.False((await Service(db).PrepareAsync()).AlreadyPrepared); Assert.Equal(3, await db.Products.CountAsync());
    }

    [PgFact]
    public async Task Missing_or_wrong_database_marker_custom_catalog_collisions_and_existing_orders_are_never_overwritten()
    {
        await using var missing = await PgDatabase.CreateAsync(); await using (var db = missing.Open())
        {
            Assert.Equal("uat_catalog_database", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).PrepareAsync())).Code);
            Assert.Equal(0, await db.Deployments.CountAsync());
            await DbGuard.AssertEnvironmentAsync(db, CommerceEnvironment.Local);
            Assert.Equal("uat_catalog_database", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).PrepareAsync())).Code);
            Assert.Equal(CommerceEnvironment.Local, (await db.Deployments.SingleAsync()).Environment);
        }
        await using var collision = await DatabaseAsync(); await using (var db = collision.Open())
        {
            await new CatalogService(db).CreateAsync(new("mango-orbit-lamp", "Custom draft", "Keep my product", ProductKind.Physical, "", new("custom-model")), "admin", default);
            Assert.Equal("uat_catalog_collision", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).PrepareAsync())).Code);
            Assert.Equal("Custom draft", (await db.Products.SingleAsync()).Name); Assert.Equal(0, await db.Variants.CountAsync());
        }
        await using var used = await DatabaseAsync(); await PaymentFixture.CreateAsync(used); await using (var db = used.Open())
        {
            Assert.Equal("uat_catalog_not_pristine", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).PrepareAsync())).Code);
            Assert.Equal(1, await db.Products.CountAsync()); Assert.Equal(0, await db.AuditEvents.CountAsync(x => x.Action == "uat-test-catalog.prepare"));
        }
        await using var binding = await DatabaseAsync(); await using (var db = binding.Open())
        {
            await new CatalogService(db).CreateAsync(new("custom-digital-star", "Custom model binding", "Keep draft binding", ProductKind.Digital, "", new("mango-star")), "admin", default);
            Assert.Equal("uat_catalog_collision", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).PrepareAsync())).Code);
            Assert.Equal("custom-digital-star", (await db.Products.SingleAsync()).Slug); Assert.Equal(0, await db.StockMovements.CountAsync());
        }
    }
}
