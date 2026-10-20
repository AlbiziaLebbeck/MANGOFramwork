using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class DigitalLineDelivery(CommerceDb db, PrivateFiles files, TimeProvider clock) : IOrderLineDelivery
{
    public ProductKind Kind => ProductKind.Digital;
    public async Task<FulfillmentItem> CreateAsync(Order order, OrderLine line, BuyerDeliverySnapshot buyer, CancellationToken cancellationToken)
    {
        var snapshot = JsonSerializer.Deserialize<PurchaseLine>(line.SnapshotJson, CatalogService.Json)
            ?? throw new CommerceException(409, "digital_snapshot_invalid", "The purchased digital delivery needs review.");
        if (snapshot.Kind != ProductKind.Digital || !Enum.IsDefined(snapshot.DigitalKind) || snapshot.Quantity != line.Quantity || snapshot.VariantId != line.VariantId)
            throw new CommerceException(409, "digital_snapshot_invalid", "The purchased digital delivery needs review.");
        if (snapshot.DigitalKind == DigitalDeliveryKind.Download)
        {
            var asset = await db.DigitalAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Key == snapshot.DigitalAssetKey && x.Ready, cancellationToken) ?? throw PrivateFiles.Unavailable();
            await files.AssertAssetAsync(asset, cancellationToken);
        }
        var existing = await db.Entitlements.Where(x => x.OrderLineId == line.Id).ToDictionaryAsync(x => x.UnitIndex, cancellationToken);
        if (existing.Values.Any(x => x.OrderId != order.Id || x.ShopperId != order.ShopperId || x.ProductId != snapshot.ProductId || x.VariantId != line.VariantId || x.UnitIndex < 0 || x.UnitIndex >= line.Quantity || x.State != EntitlementState.Revoked))
            throw new CommerceException(409, "digital_snapshot_invalid", "Existing grants without a delivery record require investigation.");
        for (var unit = 0; unit < line.Quantity; unit++)
        {
            // A refund can revoke an ungranted unit before this worker runs. Never reactivate that tombstone.
            if (existing.ContainsKey(unit)) continue;
            var grant = new Entitlement { ShopperId = order.ShopperId, OrderId = order.Id, OrderLineId = line.Id, ProductId = snapshot.ProductId, VariantId = line.VariantId,
                UnitIndex = unit, DigitalKind = snapshot.DigitalKind, DigitalAssetKey = snapshot.DigitalKind == DigitalDeliveryKind.Download ? snapshot.DigitalAssetKey : null,
                Name = line.Name, VariantName = line.VariantName, CreatedAt = clock.GetUtcNow() };
            db.Entitlements.Add(grant);
            db.EntitlementOperations.Add(new EntitlementOperation { EntitlementId = grant.Id, OperationKey = $"grant:{line.Id}:{unit}",
                RequestHash = SecretTokens.Hash($"{order.Id}:{line.Id}:{unit}"), Action = "grant", Actor = "worker", Reason = "Confirmed paid order", CreatedAt = clock.GetUtcNow() });
        }
        var remaining = line.Quantity - existing.Count;
        return new FulfillmentItem { OrderId = order.Id, OrderLineId = line.Id, Kind = line.Kind, Quantity = remaining > 0 ? remaining : line.Quantity, DeliveryMethod = "digital",
            State = remaining > 0 ? FulfillmentState.Delivered : FulfillmentState.Cancelled, DeliveredAt = remaining > 0 ? clock.GetUtcNow() : null, UpdatedAt = clock.GetUtcNow() };
    }
}

public sealed record EntitlementView(Guid Id, Guid OrderId, Guid OrderLineId, Guid ProductId, Guid VariantId, int UnitIndex, string Name, string VariantName,
    string Kind, string State, bool CanUse, bool CanDownload, DateTimeOffset GrantedAt);
public sealed record InventoryPage(EntitlementView[] Items, Guid? NextCursor);
public sealed record OpenedDigitalFile(FileStream Stream, string FileName);
public sealed record RevokeEntitlementInput(string OperationId, string Reason);

public sealed class EntitlementService(CommerceDb db, PrivateFiles files, TimeProvider clock)
{
    public async Task RevokeAsync(Guid entitlementId, RevokeEntitlementInput input, string actor, CancellationToken cancellationToken)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(input.OperationId ?? "", "^[a-zA-Z0-9:_-]{1,100}$") || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500 || input.Reason.Any(char.IsControl))
            throw new CommerceException(400, "revocation_invalid", "A stable operation ID and reason are required.");
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Revocation must own its transaction.");
        db.ChangeTracker.Clear();
        var orderId = await db.Entitlements.AsNoTracking().Where(x => x.Id == entitlementId).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE").ToListAsync(cancellationToken);
        var key = "revoke:" + input.OperationId; var requestHash = SecretTokens.Hash($"{entitlementId}:{input.Reason}");
        var operation = await db.EntitlementOperations.SingleOrDefaultAsync(x => x.OperationKey == key, cancellationToken);
        if (operation is not null)
        {
            if (operation.EntitlementId != entitlementId || operation.RequestHash != requestHash) throw new CommerceException(409, "idempotency_conflict", "This revocation ID already refers to another operation.");
            return;
        }
        var grant = await db.Entitlements.SingleAsync(x => x.Id == entitlementId, cancellationToken);
        if (grant.State != EntitlementState.Revoked) { grant.State = EntitlementState.Revoked; grant.RevokedAt = clock.GetUtcNow(); grant.RevocationReason = input.Reason; }
        db.EntitlementOperations.Add(new EntitlementOperation { EntitlementId = grant.Id, OperationKey = key, RequestHash = requestHash, Action = "revoke", Actor = actor, Reason = input.Reason });
        db.AuditEvents.Add(new AuditEvent { Actor = actor, Action = "entitlement.revoke", Resource = $"entitlement:{grant.Id}", DataJson = JsonSerializer.Serialize(new { input.OperationId, input.Reason }) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task<InventoryPage> ListAsync(Guid shopperId, int limit, Guid? cursor, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new CommerceException(400, "page_invalid", "Page size must be1–100.");
        var query = from grant in db.Entitlements.AsNoTracking() join order in db.Orders.AsNoTracking() on grant.OrderId equals order.Id
                    where grant.ShopperId == shopperId select new { Grant = grant, Order = order };
        if (cursor is not null) query = query.Where(x => x.Grant.Id.CompareTo(cursor.Value) > 0);
        var rows = await query.OrderBy(x => x.Grant.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        var page = rows.Take(limit).Select(x =>
        {
            var active = x.Grant.State == EntitlementState.Active && x.Order.PaymentState == PaymentState.Paid && x.Order.State is OrderState.Open or OrderState.Completed;
            return new EntitlementView(x.Grant.Id, x.Order.Id, x.Grant.OrderLineId, x.Grant.ProductId, x.Grant.VariantId, x.Grant.UnitIndex, x.Grant.Name, x.Grant.VariantName,
                x.Grant.DigitalKind.ToString(), x.Grant.State.ToString(), active, active && x.Grant.DigitalKind == DigitalDeliveryKind.Download, x.Grant.CreatedAt);
        }).ToArray();
        return new(page, rows.Length > limit ? page[^1].Id : null);
    }

    public async Task<OpenedDigitalFile> DownloadAsync(Guid shopperId, Guid entitlementId, string requestUid, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var orderId = await db.Entitlements.AsNoTracking().Where(x => x.Id == entitlementId && x.ShopperId == shopperId).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        await FulfillmentService.AssertDeliverableAsync(db, order, cancellationToken);
        var grant = await db.Entitlements.SingleAsync(x => x.Id == entitlementId && x.ShopperId == shopperId, cancellationToken);
        if (grant.State != EntitlementState.Active || grant.DigitalKind != DigitalDeliveryKind.Download || grant.DigitalAssetKey is null)
            throw new CommerceException(409, "download_not_available", "This entitlement does not allow a download.");
        var asset = await db.DigitalAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Key == grant.DigitalAssetKey && x.Ready, cancellationToken) ?? throw PrivateFiles.Unavailable();
        await files.AssertAssetAsync(asset, cancellationToken);
        grant.DownloadRequestedAt ??= clock.GetUtcNow(); grant.DownloadRequests = checked(grant.DownloadRequests + 1);
        db.DownloadEvents.Add(new DigitalDownloadEvent { EntitlementId = grant.Id, ShopperId = shopperId, RequestUid = requestUid, CreatedAt = clock.GetUtcNow() });
        var stream = new FileStream(files.PathFor("assets", asset.Key, ".asset"), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return new(stream, asset.FileName); }
        catch { await stream.DisposeAsync(); throw; }
    }

    public async Task<object> ValidateGameAsync(Guid shopperId, Guid entitlementId, CancellationToken cancellationToken)
    {
        var result = await (from grant in db.Entitlements.AsNoTracking() join order in db.Orders.AsNoTracking() on grant.OrderId equals order.Id
                            where grant.Id == entitlementId && grant.ShopperId == shopperId && grant.State == EntitlementState.Active && order.PaymentState == PaymentState.Paid &&
                                (order.State == OrderState.Open || order.State == OrderState.Completed) &&
                                (from allocation in db.Allocations join bank in db.BankTransactions on allocation.BankTransactionId equals bank.Id
                                 where allocation.OrderId == order.Id && allocation.Primary && !bank.Voided select allocation.Id).Any()
                            select new { grant.Id, grant.ProductId, grant.VariantId }).SingleOrDefaultAsync(cancellationToken);
        return result is null ? new { owned = false, entitlementId } : (object)new { owned = true, entitlementId = result.Id, result.ProductId, result.VariantId };
    }

    private static CommerceException Missing() => new(404, "not_found", "The digital entitlement was not found.");
}
