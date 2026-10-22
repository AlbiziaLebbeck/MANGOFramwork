using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public sealed record DashboardOrder(Guid Id, string Number, long AmountMinor, string PaymentState, string State,
    DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt);
public sealed record DashboardNotice(Guid Id, string? OrderNumber, Guid? OrderId, string Source, string Outcome,
    string RequestUid, string? PeerIp, string? QrId, string? InstructionId, string? AckCode, int? DeliveryCount, DateTimeOffset CreatedAt);

// Explicit projections exclude raw bank payloads, buyer snapshots, payer details and credentials.
public sealed class PaymentDashboardQueries(CommerceDb db, TimeProvider clock)
{
    private static string Search(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length > 100 || text.Any(char.IsControl)) throw new CommerceException(400, "dashboard_search_invalid", "Use a search term of at most 100 characters.");
        return text;
    }

    public async Task<object> OverviewAsync(string? search, string? state, Guid? before, CancellationToken ct)
    {
        var term = Search(search);
        if (state is not (null or "" or "all" or "paid" or "unpaid" or "review"))
            throw new CommerceException(400, "dashboard_state_invalid", "Unknown payment state filter.");
        var orders = db.Orders.AsNoTracking().AsQueryable();
        if (term.Length > 0)
            orders = orders.Where(o => o.Number.Contains(term) || db.Attempts.Any(a => a.OrderId == o.Id && (a.QrId.Contains(term) || a.Ref1.Contains(term) || a.Ref2.Contains(term))) ||
                db.Allocations.Any(a => a.OrderId == o.Id && db.BankTransactions.Any(b => b.Id == a.BankTransactionId && b.InstructionId.Contains(term))));
        if (state == "paid") orders = orders.Where(o => o.PaymentState == PaymentState.Paid);
        if (state == "unpaid") orders = orders.Where(o => o.PaymentState == PaymentState.Unpaid);
        if (state == "review") orders = orders.Where(o => o.PaymentState == PaymentState.Review);
        if (before is { } cursor) orders = orders.Where(o => o.Id.CompareTo(cursor) < 0);
        var rows = await orders.OrderByDescending(o => o.Id).Take(51)
            .Select(o => new DashboardOrder(o.Id, o.Number, o.TotalMinor, o.PaymentState.ToString(), o.State.ToString(), o.CreatedAt, o.ConfirmedAt)).ToArrayAsync(ct);
        return new { environment = "Uat", fetchedAt = clock.GetUtcNow(), summary = new {
            orders = await db.Orders.CountAsync(ct), paid = await db.Orders.CountAsync(o => o.PaymentState == PaymentState.Paid, ct),
            notifications = await db.Observations.CountAsync(o => o.Source == "Callback", ct), tests = await db.BankTestCases.CountAsync(ct) },
            orders = rows.Take(50), nextCursor = rows.Length > 50 ? rows[49].Id : (Guid?)null };
    }

    public async Task<object> NotificationsAsync(string? search, Guid? before, CancellationToken ct)
    {
        var term = Search(search);
        var query = from observation in db.Observations.AsNoTracking()
                    where observation.Source == "Callback"
                    join attempt in db.Attempts on observation.AttemptId equals attempt.Id into attempts
                    from attempt in attempts.DefaultIfEmpty()
                    join order in db.Orders on attempt.OrderId equals order.Id into orders
                    from order in orders.DefaultIfEmpty()
                    join receipt in db.CallbackReceipts on observation.Id equals receipt.ObservationId into receipts
                    from receipt in receipts.DefaultIfEmpty()
                    select new { observation, attempt, order, receipt };
        if (term.Length > 0) query = query.Where(x => (x.order != null && x.order.Number.Contains(term)) ||
            (x.attempt != null && x.attempt.QrId.Contains(term)) || (x.receipt != null && x.receipt.InstructionId.Contains(term)) || x.observation.CorrelationId.Contains(term));
        if (before is { } cursor) query = query.Where(x => x.observation.Id.CompareTo(cursor) < 0);
        var rows = await query.OrderByDescending(x => x.observation.Id).Take(51).Select(x => new {
            x.observation.Id, OrderNumber = x.order == null ? null : x.order.Number, OrderId = x.order == null ? (Guid?)null : x.order.Id,
            x.observation.Source, x.observation.Outcome, RequestUid = x.observation.CorrelationId, x.observation.MetadataJson,
            QrId = x.attempt == null ? null : x.attempt.QrId, InstructionId = x.receipt == null ? null : x.receipt.InstructionId,
            AckCode = x.receipt == null ? null : x.receipt.AckCode, DeliveryCount = x.receipt == null ? (int?)null : x.receipt.DeliveryCount, x.observation.CreatedAt }).ToArrayAsync(ct);
        return new { fetchedAt = clock.GetUtcNow(), items = rows.Take(50).Select(x => new DashboardNotice(x.Id, x.OrderNumber, x.OrderId,
            x.Source, x.Outcome, x.RequestUid, Peer(x.MetadataJson), x.QrId, x.InstructionId, x.AckCode, x.DeliveryCount, x.CreatedAt)),
            nextCursor = rows.Length > 50 ? rows[49].Id : (Guid?)null };
    }

    public async Task<object> OrderAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Where(o => o.Id == id)
            .Select(o => new DashboardOrder(o.Id, o.Number, o.TotalMinor, o.PaymentState.ToString(), o.State.ToString(), o.CreatedAt, o.ConfirmedAt)).SingleOrDefaultAsync(ct)
            ?? throw new CommerceException(404, "order_not_found", "Order not found.");
        var attempts = await db.Attempts.AsNoTracking().Where(a => a.OrderId == id).OrderByDescending(a => a.Id).Take(50)
            .Select(a => new { a.Id, a.QrId, a.Ref1, a.Ref2, a.ExpectedMinor, State = a.State.ToString(), a.CreatedAt, a.ExpiresAt }).ToArrayAsync(ct);
        var observations = await db.Observations.AsNoTracking().Where(o => db.Attempts.Any(a => a.Id == o.AttemptId && a.OrderId == id))
            .OrderByDescending(o => o.Id).Take(100)
            .Select(o => new { o.Id, o.Source, o.Outcome, RequestUid = o.CorrelationId, o.CreatedAt, o.MetadataJson }).ToArrayAsync(ct);
        var observationIds = observations.Select(o => o.Id).ToArray();
        var receipts = await db.CallbackReceipts.AsNoTracking().Where(r => observationIds.Contains(r.ObservationId))
            .Select(r => new { r.InstructionId, r.AckCode, r.DeliveryCount, r.LastReceivedAt, r.ObservationId }).ToArrayAsync(ct);
        var payments = await (from allocation in db.Allocations.AsNoTracking() join bank in db.BankTransactions on allocation.BankTransactionId equals bank.Id
            where allocation.OrderId == id select new { bank.InstructionId, bank.QrId, bank.AmountMinor, bank.Voided, bank.CreatedAt }).ToArrayAsync(ct);
        var fulfillments = await db.Fulfillments.AsNoTracking().Where(f => f.OrderId == id).Select(f => new { State = f.State.ToString(), Kind = f.Kind.ToString(), f.CreatedAt, f.DeliveredAt }).ToArrayAsync(ct);
        var documents = await db.Documents.AsNoTracking().Where(d => d.OrderId == id).Select(d => new { d.Number, Kind = d.Kind.ToString(), State = d.State.ToString(), d.CreatedAt }).ToArrayAsync(ct);
        return new { order, attempts, evidence = observations.Select(o => new { o.Id, o.Source, o.Outcome, o.RequestUid, o.CreatedAt, peerIp = Peer(o.MetadataJson) }),
            callbacks = receipts, payments, fulfillment = fulfillments, documents, fetchedAt = clock.GetUtcNow() };
    }

    private static string? Peer(string json)
    {
        try { using var document = JsonDocument.Parse(json); return document.RootElement.TryGetProperty("peerIp", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null; }
        catch (JsonException) { return null; }
    }
}
