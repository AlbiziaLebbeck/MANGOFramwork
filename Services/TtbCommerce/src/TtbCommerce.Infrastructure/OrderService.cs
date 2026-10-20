using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class OrderService(CommerceDb db, CartService carts, Numbering numbering, CommerceOptions options, TimeProvider clock, BankReadiness readiness)
{
    public async Task<OrderView> CreateAsync(Guid shopperId, CreateOrderInput input, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(idempotencyKey ?? "", "^[A-Za-z0-9:_-]{1,100}$", RegexOptions.CultureInvariant))
            throw new CommerceException(400, "idempotency_key_invalid", "Provide a stable Idempotency-Key for this checkout.");
        var requestHash = SecretTokens.Hash($"order:v1:{input.QuoteId:N}");
        var lockKey = $"order:{shopperId}:{idempotencyKey}";
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        var existing = await db.Orders.SingleOrDefaultAsync(x => x.ShopperId == shopperId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash) throw new CommerceException(409, "idempotency_conflict", "This checkout key already refers to another request.");
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(shopperId, existing.Id, cancellationToken);
        }
        readiness.AssertReady();
        // Quote locks serialize distinct checkout keys attempting to consume the same quote.
        var quote = (await db.Quotes.FromSqlInterpolated($"SELECT * FROM ttb.\"Quotes\" WHERE \"Id\" = {input.QuoteId} AND \"ShopperId\" = {shopperId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault() ?? throw Missing();
        if (quote.ConsumedByOrderId is not null) throw new CommerceException(409, "quote_consumed", "This quote has already been used for an order.");
        if (quote.ExpiresAt <= clock.GetUtcNow()) throw new CommerceException(409, "quote_expired", "Create a new quote before placing the order.");
        var cart = await carts.LockAsync(shopperId, cancellationToken);
        CartService.CheckVersion(cart, quote.CartVersion);
        var snapshot = PurchaseCodec.Read(quote);
        if (snapshot.DeliveryPolicyHash != CartService.DeliveryPolicyHash(options)) throw Changed();
        var productIds = snapshot.Lines.Select(x => x.ProductId).Distinct().Order().ToArray();
        var variantIds = snapshot.Lines.Select(x => x.VariantId).Distinct().Order().ToArray();
        // Every multi-row stock/catalog path uses the same product-then-variant UUID order.
        var products = await db.Products.FromSqlInterpolated($"SELECT * FROM ttb.\"Products\" WHERE \"Id\" = ANY({productIds}) ORDER BY \"Id\" FOR UPDATE").ToDictionaryAsync(x => x.Id, cancellationToken);
        var variants = await db.Variants.FromSqlInterpolated($"SELECT * FROM ttb.\"Variants\" WHERE \"Id\" = ANY({variantIds}) ORDER BY \"Id\" FOR UPDATE").ToDictionaryAsync(x => x.Id, cancellationToken);
        if (snapshot.Lines.Length == 0 || snapshot.Lines.Any(line => !products.TryGetValue(line.ProductId, out var p) || !p.Published || p.Version != line.ProductVersion ||
            !variants.TryGetValue(line.VariantId, out var v) || v.ProductId != line.ProductId || !v.Active || v.PriceMinor != line.UnitPriceMinor || v.TrackStock != line.TrackStock)) throw Changed();
        var date = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Numbering.Bangkok).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var sequence = await numbering.NextAsync($"order:{date}", 999999, cancellationToken);
        var environmentPrefix = options.Environment switch { CommerceEnvironment.Production => "P", CommerceEnvironment.Uat => "T", _ => "L" };
        var order = new Order { ShopperId = shopperId, QuoteId = quote.Id, Number = $"{environmentPrefix}{date}{sequence:D6}", IdempotencyKey = idempotencyKey!, RequestHash = requestHash,
            SubtotalMinor = quote.SubtotalMinor, ShippingMinor = quote.ShippingMinor, TotalMinor = quote.TotalMinor, ExpiresAt = clock.GetUtcNow().AddMinutes(options.PaymentMinutes),
            BuyerSnapshotJson = JsonSerializer.Serialize(new BuyerDeliverySnapshot(1, snapshot.Buyer, snapshot.Delivery), CatalogService.Json) };
        db.Orders.Add(order);
        foreach (var line in snapshot.Lines)
        {
            var orderLine = new OrderLine { OrderId = order.Id, VariantId = line.VariantId, Kind = line.Kind, Sku = line.Sku, Name = line.Name, VariantName = line.VariantName,
                Quantity = line.Quantity, UnitPriceMinor = line.UnitPriceMinor, TotalMinor = line.TotalMinor, SnapshotJson = JsonSerializer.Serialize(line, CatalogService.Json) };
            db.OrderLines.Add(orderLine);
            if (line.TrackStock)
            {
                var updated = await db.Variants.Where(x => x.Id == line.VariantId && x.OnHand - x.Reserved >= line.Quantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved + line.Quantity), cancellationToken);
                if (updated != 1) throw new CommerceException(409, "insufficient_stock", "Some items sold out before checkout. Review your cart.");
                db.Reservations.Add(new StockReservation { OrderLineId = orderLine.Id, VariantId = line.VariantId, Quantity = line.Quantity, ExpiresAt = order.ExpiresAt });
                db.StockMovements.Add(new StockMovement { VariantId = line.VariantId, OrderLineId = orderLine.Id, EffectKey = $"reserve:{orderLine.Id}", ReservedDelta = line.Quantity, Reason = "Order reservation", Actor = $"shopper:{shopperId}" });
            }
        }
        quote.ConsumedByOrderId = order.Id;
        await db.CartLines.Where(x => x.CartId == cart.Id).ExecuteDeleteAsync(cancellationToken);
        cart.Version++; cart.UpdatedAt = clock.GetUtcNow();
        var expiry = DurableWork.Enqueue($"expire:{order.Id}", "order.expire", JsonSerializer.Serialize(new { orderId = order.Id }));
        expiry.AvailableAt = order.ExpiresAt; db.Jobs.Add(expiry);
        db.AuditEvents.Add(new AuditEvent { Actor = $"shopper:{shopperId}", Action = "order.create", Resource = $"order:{order.Id}" });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(shopperId, order.Id, cancellationToken);
    }

    public async Task<OrderView> GetAsync(Guid shopperId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId && x.ShopperId == shopperId, cancellationToken) ?? throw Missing();
        var lines = await db.OrderLines.AsNoTracking().Where(x => x.OrderId == orderId).OrderBy(x => x.Id).ToArrayAsync(cancellationToken);
        return View(order, lines);
    }

    public async Task<OrderPage> ListAsync(Guid shopperId, int limit, Guid? cursor, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 50) throw new CommerceException(400, "page_invalid", "Page size must be1–50.");
        var query = db.Orders.AsNoTracking().Where(x => x.ShopperId == shopperId);
        if (cursor is not null) query = query.Where(x => x.Id.CompareTo(cursor.Value) < 0);
        var orders = await query.OrderByDescending(x => x.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        var page = orders.Take(limit).ToArray(); var ids = page.Select(x => x.Id).ToArray();
        var lines = await db.OrderLines.AsNoTracking().Where(x => ids.Contains(x.OrderId)).OrderBy(x => x.Id).ToArrayAsync(cancellationToken);
        return new(page.Select(x => View(x, lines.Where(l => l.OrderId == x.Id))).ToArray(), orders.Length > limit ? page[^1].Id : null);
    }

    public async Task CancelAsync(Guid shopperId, Guid orderId, CancellationToken cancellationToken) => await CloseAsync(orderId, shopperId, false, cancellationToken);
    public async Task ExpireAsync(Guid orderId, CancellationToken cancellationToken) => await CloseAsync(orderId, null, true, cancellationToken);

    private async Task CloseAsync(Guid orderId, Guid? shopperId, bool expiredOnly, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();
        if (order is null || shopperId is not null && order.ShopperId != shopperId) throw Missing();
        if (expiredOnly && (order.ExpiresAt > clock.GetUtcNow() || order.PaymentState != PaymentState.Unpaid || order.State != OrderState.Open)) return;
        if (order.State == OrderState.Cancelled) return;
        if (order.PaymentState != PaymentState.Unpaid || order.State != OrderState.Open)
            throw new CommerceException(409, "cannot_cancel", "A paid or reviewed order needs a finance action.");
        var reservations = await (from r in db.Reservations join l in db.OrderLines on r.OrderLineId equals l.Id
                                  where l.OrderId == orderId && r.State == ReservationState.Reserved orderby r.VariantId select r).ToArrayAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            var quantity = reservation.Quantity;
            var changed = await db.Variants.Where(x => x.Id == reservation.VariantId && x.Reserved >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved - quantity), cancellationToken);
            if (changed != 1) throw new InvalidOperationException("Reserved inventory is inconsistent; closure must be reviewed.");
            reservation.State = ReservationState.Released;
            db.StockMovements.Add(new StockMovement { VariantId = reservation.VariantId, OrderLineId = reservation.OrderLineId, EffectKey = $"release:{reservation.OrderLineId}",
                ReservedDelta = -quantity, Reason = expiredOnly ? "Order expired" : "Order cancelled", Actor = shopperId is null ? "worker" : $"shopper:{shopperId}" });
        }
        order.State = OrderState.Cancelled;
        await db.Attempts.Where(x => x.OrderId == orderId && x.State == AttemptState.AwaitingPayment)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, AttemptState.Expired), cancellationToken);
        db.AuditEvents.Add(new AuditEvent { Actor = shopperId is null ? "worker" : $"shopper:{shopperId}", Action = expiredOnly ? "order.expire" : "order.cancel", Resource = $"order:{orderId}" });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static OrderView View(Order order, IEnumerable<OrderLine> lines)
    {
        var buyer = JsonSerializer.Deserialize<BuyerDeliverySnapshot>(order.BuyerSnapshotJson, CatalogService.Json)!;
        var views = lines.Select(l =>
        {
            var snapshot = JsonSerializer.Deserialize<PurchaseLine>(l.SnapshotJson, CatalogService.Json)!;
            return new OrderLineView(l.Id, snapshot.ProductId, l.VariantId, l.Sku, l.Name, l.VariantName, l.Kind.ToString(), l.Quantity, l.UnitPriceMinor, l.TotalMinor, snapshot.ThumbnailUrl);
        }).ToArray();
        return new(order.Id, order.Number, order.State.ToString(), order.PaymentState.ToString(), order.Currency, order.SubtotalMinor, order.ShippingMinor, order.TotalMinor,
            views, buyer.Buyer, buyer.Delivery, order.CreatedAt, order.ExpiresAt, order.ConfirmedAt, order.State == OrderState.Open && order.PaymentState == PaymentState.Unpaid);
    }

    private static CommerceException Missing() => new(404, "not_found", "The order or quote was not found.");
    private static CommerceException Changed() => new(409, "quote_changed", "Product or delivery details changed. Review a new quote before placing the order.");
}
