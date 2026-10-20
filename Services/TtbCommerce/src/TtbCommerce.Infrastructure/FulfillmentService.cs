using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public interface IOrderLineDelivery
{
    ProductKind Kind { get; }
    Task<FulfillmentItem> CreateAsync(Order order, OrderLine line, BuyerDeliverySnapshot buyer, CancellationToken cancellationToken);
}

public sealed class PhysicalLineDelivery(TimeProvider clock) : IOrderLineDelivery
{
    public ProductKind Kind => ProductKind.Physical;
    public Task<FulfillmentItem> CreateAsync(Order order, OrderLine line, BuyerDeliverySnapshot buyer, CancellationToken cancellationToken)
    {
        if (buyer.Delivery.Method is not ("pickup" or "shipping")) throw new CommerceException(409, "delivery_configuration_invalid", "The physical delivery method needs review.");
        return Task.FromResult(new FulfillmentItem { OrderId = order.Id, OrderLineId = line.Id, Kind = line.Kind, Quantity = line.Quantity,
            DeliveryMethod = buyer.Delivery.Method, UpdatedAt = clock.GetUtcNow() });
    }
}

public sealed class FulfillmentDispatchHandler(FulfillmentService service) : ICommerceJobHandler
{
    public string Kind => "fulfillment.dispatch";
    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payloadJson);
        await service.DispatchAsync(document.RootElement.GetProperty("orderId").GetGuid(), document.RootElement.GetProperty("lineId").GetGuid(), cancellationToken);
    }
}

public sealed class FulfillmentService(CommerceDb db, IEnumerable<IOrderLineDelivery> handlers, TimeProvider clock)
{
    public async Task<FulfillmentView> DispatchAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Delivery dispatch must own its transaction.");
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = await LockOrderAsync(orderId, cancellationToken);
        var line = await db.OrderLines.SingleOrDefaultAsync(x => x.Id == lineId && x.OrderId == orderId, cancellationToken) ?? throw Missing();
        var existing = await db.Fulfillments.SingleOrDefaultAsync(x => x.OrderLineId == lineId, cancellationToken);
        if (existing is not null) return View(existing, line);
        await AssertDeliverableAsync(db, order, cancellationToken);
        var handler = handlers.SingleOrDefault(x => x.Kind == line.Kind) ?? throw new CommerceException(409, "delivery_handler_unavailable", "The delivery handler is not ready.");
        var buyer = JsonSerializer.Deserialize<BuyerDeliverySnapshot>(order.BuyerSnapshotJson, CatalogService.Json) ?? throw new InvalidOperationException("Missing buyer snapshot.");
        var item = await handler.CreateAsync(order, line, buyer, cancellationToken);
        if (line.Kind == ProductKind.Physical)
        {
            var returned = await db.GoodsReturns.Where(x => x.OrderLineId == line.Id).SumAsync(x => x.Quantity, cancellationToken);
            if (returned < 0 || returned > line.Quantity) throw new CommerceException(409, "goods_return_quantity_invalid", "Returned units conflict with the immutable purchase.");
            item.Quantity = line.Quantity - returned;
            if (item.Quantity == 0) { item.Quantity = line.Quantity; item.State = FulfillmentState.Cancelled; item.OperatorNote = "All committed units were explicitly returned before dispatch"; }
        }
        db.Fulfillments.Add(item);
        db.AuditEvents.Add(new AuditEvent { Actor = "worker", Action = "fulfillment.dispatch", Resource = $"order-line:{lineId}" });
        await db.SaveChangesAsync(cancellationToken);
        await RefreshCompletionAsync(db, order, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return View(item, line);
    }

    public async Task<FulfillmentView[]> OwnAsync(Guid shopperId, Guid orderId, CancellationToken cancellationToken)
    {
        if (!await db.Orders.AnyAsync(x => x.Id == orderId && x.ShopperId == shopperId, cancellationToken)) throw Missing();
        var rows = await (from f in db.Fulfillments.AsNoTracking() join l in db.OrderLines.AsNoTracking() on f.OrderLineId equals l.Id
                          where f.OrderId == orderId orderby f.Id select new { Item = f, Line = l }).ToArrayAsync(cancellationToken);
        return rows.Select(x => View(x.Item, x.Line)).ToArray();
    }

    public async Task<FulfillmentQueuePage> QueueAsync(int limit, Guid? cursor, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new CommerceException(400, "page_invalid", "Page size must be1–100.");
        var query = from f in db.Fulfillments.AsNoTracking() join l in db.OrderLines.AsNoTracking() on f.OrderLineId equals l.Id
                    join o in db.Orders.AsNoTracking() on f.OrderId equals o.Id
                    where f.Kind == ProductKind.Physical && f.State != FulfillmentState.Delivered && f.State != FulfillmentState.Cancelled && o.PaymentState == PaymentState.Paid
                    select new { Item = f, Line = l, Order = o };
        if (cursor is not null) query = query.Where(x => x.Item.Id.CompareTo(cursor.Value) > 0);
        var rows = await query.OrderBy(x => x.Item.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        var page = rows.Take(limit).Select(x =>
        {
            var snapshot = JsonSerializer.Deserialize<BuyerDeliverySnapshot>(x.Order.BuyerSnapshotJson, CatalogService.Json)!;
            return new FulfillmentQueueItem(x.Order.Id, x.Order.Number, x.Order.State.ToString(), snapshot.Buyer, snapshot.Delivery, View(x.Item, x.Line));
        }).ToArray();
        return new(page, rows.Length > limit ? page[^1].Item.Id : null);
    }

    public async Task<FulfillmentView> ActAsync(Guid fulfillmentId, FulfillmentActionInput input, string actor, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Delivery actions must own their transaction.");
        db.ChangeTracker.Clear();
        if (input.Note?.Length > 500 || input.Note?.Any(char.IsControl) == true || input.Carrier?.Length > 100 || input.Carrier?.Any(char.IsControl) == true ||
            input.TrackingNumber?.Length > 100 || input.TrackingNumber?.Any(char.IsControl) == true)
            throw new CommerceException(400, "fulfillment_input_invalid", "Delivery details are too long or invalid.");
        var orderId = await db.Fulfillments.AsNoTracking().Where(x => x.Id == fulfillmentId).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = await LockOrderAsync(orderId, cancellationToken);
        await AssertDeliverableAsync(db, order, cancellationToken);
        var item = (await db.Fulfillments.FromSqlInterpolated($"SELECT * FROM ttb.\"Fulfillments\" WHERE \"Id\" = {fulfillmentId} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        if (item.Kind != ProductKind.Physical || item.Version != input.ExpectedVersion) throw new CommerceException(409, "fulfillment_changed", "Reload the delivery record before changing it.");
        var target = (item.State, item.DeliveryMethod, input.Action) switch
        {
            (FulfillmentState.Pending, _, "prepare") => FulfillmentState.Preparing,
            (FulfillmentState.Preparing, "pickup", "ready-pickup") => FulfillmentState.ReadyForPickup,
            (FulfillmentState.Preparing, "shipping", "ship") => FulfillmentState.Shipped,
            (FulfillmentState.ReadyForPickup, "pickup", "deliver") => FulfillmentState.Delivered,
            (FulfillmentState.Shipped, "shipping", "deliver") => FulfillmentState.Delivered,
            _ => throw new CommerceException(409, "fulfillment_transition_invalid", "That delivery transition is unavailable.")
        };
        if (target == FulfillmentState.Shipped)
        {
            if (string.IsNullOrWhiteSpace(input.Carrier) || string.IsNullOrWhiteSpace(input.TrackingNumber)) throw new CommerceException(400, "tracking_required", "Provide the carrier and tracking/reference number.");
            item.Carrier = input.Carrier.Trim(); item.TrackingNumber = input.TrackingNumber.Trim(); item.ShippedAt = clock.GetUtcNow();
        }
        item.State = target; item.Version++; item.OperatorNote = input.Note; item.UpdatedAt = clock.GetUtcNow();
        if (target == FulfillmentState.Delivered) item.DeliveredAt = clock.GetUtcNow();
        db.AuditEvents.Add(new AuditEvent { Actor = actor, Action = $"fulfillment.{input.Action}", Resource = $"fulfillment:{item.Id}",
            DataJson = JsonSerializer.Serialize(new { item.Version, state = item.State.ToString(), input.Note }) });
        await db.SaveChangesAsync(cancellationToken); await RefreshCompletionAsync(db, order, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return View(item, await db.OrderLines.SingleAsync(x => x.Id == item.OrderLineId, cancellationToken));
    }

    internal static async Task AssertDeliverableAsync(CommerceDb db, Order order, CancellationToken cancellationToken)
    {
        if (order.PaymentState != PaymentState.Paid || order.State is not (OrderState.Open or OrderState.Completed) ||
            !await (from allocation in db.Allocations join bank in db.BankTransactions on allocation.BankTransactionId equals bank.Id
                    where allocation.OrderId == order.Id && allocation.Primary && !bank.Voided select allocation.Id).AnyAsync(cancellationToken))
            throw new CommerceException(409, "fulfillment_not_ready", "Delivery requires confirmed payment without a finance hold.");
    }

    internal static async Task RefreshCompletionAsync(CommerceDb db, Order order, CancellationToken cancellationToken)
    {
        var lines = await db.OrderLines.CountAsync(x => x.OrderId == order.Id, cancellationToken);
        if (lines > 0 && await db.Fulfillments.CountAsync(x => x.OrderId == order.Id && (x.State == FulfillmentState.Delivered || x.State == FulfillmentState.Cancelled), cancellationToken) == lines)
        { order.State = OrderState.Completed; await db.SaveChangesAsync(cancellationToken); }
    }

    private async Task<Order> LockOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault() ?? throw Missing();
    private static FulfillmentView View(FulfillmentItem f, OrderLine l) => new(f.Id, l.Id, l.Sku, l.Name, f.Kind.ToString(), f.Quantity, f.DeliveryMethod, f.State.ToString(), f.Version,
        f.Carrier, f.TrackingNumber, f.ShippedAt, f.DeliveredAt);
    private static CommerceException Missing() => new(404, "not_found", "The order or delivery was not found.");
}
