using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class GameTicketService(CommerceDb db, PrivateStorageOptions options, TimeProvider clock)
{
    public async Task<GameTicketView> IssueAsync(Guid shopperId, Guid sessionId, GameTicketInput input, CancellationToken cancellationToken)
    {
        AssertProfile(input.ServerId); ValidateChallenge(input.Challenge);
        Reset(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var session = await LockSessionAsync(shopperId, sessionId, cancellationToken);
        var lockKey = $"game-ticket:{shopperId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        var now = clock.GetUtcNow();
        if (await db.GameTickets.CountAsync(x => x.ShopperId == shopperId && x.CreatedAt > now.AddMinutes(-1), cancellationToken) >= 20)
            throw new CommerceException(429, "game_ticket_limit", "Wait before requesting more game authorizations.");
        var grant = await ReadGrantAsync(shopperId, session.Id, input.EntitlementId, cancellationToken);
        if (!grant.Owned) throw new CommerceException(404, "not_found", "The usable digital entitlement was not found.");
        var token = SecretTokens.New(); var expiry = now.AddSeconds(60) < session.ExpiresAt ? now.AddSeconds(60) : session.ExpiresAt;
        db.GameTickets.Add(new GameUseTicket { ShopperId = shopperId, SessionId = sessionId, EntitlementId = input.EntitlementId, ServerId = input.ServerId,
            TokenHash = SecretTokens.Hash(token), ChallengeHash = SecretTokens.Hash(input.Challenge), ExpiresAt = expiry, CreatedAt = now });
        db.AuditEvents.Add(new AuditEvent { Actor = $"shopper:{shopperId}", Action = "game.ticket.issue", Resource = $"entitlement:{input.EntitlementId}" });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return new(token, expiry);
    }

    public async Task<GameGrant> ConsumeAsync(GameTicketConsume input, CancellationToken cancellationToken)
    {
        AssertProfile(input.ServerId); ValidateChallenge(input.Challenge);
        if (input.Ticket is null || !Regex.IsMatch(input.Ticket, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)) throw InvalidTicket();
        Reset(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var hash = SecretTokens.Hash(input.Ticket);
        var ticket = (await db.GameTickets.FromSqlInterpolated($"SELECT * FROM ttb.\"GameTickets\" WHERE \"TokenHash\" = {hash} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();
        if (ticket is null || ticket.UsedAt is not null || ticket.ExpiresAt <= clock.GetUtcNow() || ticket.ServerId != input.ServerId ||
            ticket.EntitlementId != input.EntitlementId || ticket.ChallengeHash != SecretTokens.Hash(input.Challenge)) throw InvalidTicket();
        await LockSessionAsync(ticket.ShopperId, ticket.SessionId, cancellationToken);
        var grant = await ReadGrantAsync(ticket.ShopperId, ticket.SessionId, ticket.EntitlementId, cancellationToken);
        ticket.UsedAt = clock.GetUtcNow();
        db.AuditEvents.Add(new AuditEvent { Actor = "game:" + input.ServerId, Action = grant.Owned ? "game.ticket.consume" : "game.ticket.denied", Resource = $"entitlement:{ticket.EntitlementId}" });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return grant;
    }

    public async Task<GameGrant> LeaseAsync(GameLeaseInput input, CancellationToken cancellationToken)
    {
        AssertProfile(input.ServerId); Reset(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.SessionId && x.ShopperId == input.ShopperId && x.RevokedAt == null && x.ExpiresAt > clock.GetUtcNow(), cancellationToken);
        if (session is null) return new(false, input.EntitlementId);
        await LockSessionAsync(input.ShopperId, input.SessionId, cancellationToken);
        var grant = await ReadGrantAsync(input.ShopperId, input.SessionId, input.EntitlementId, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return grant;
    }

    private async Task<GameGrant> ReadGrantAsync(Guid shopperId, Guid sessionId, Guid entitlementId, CancellationToken cancellationToken)
    {
        var orderId = await db.Entitlements.AsNoTracking().Where(x => x.Id == entitlementId && x.ShopperId == shopperId).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(cancellationToken);
        if (orderId is null) return new(false, entitlementId);
        var order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId.Value} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        if (order.PaymentState != PaymentState.Paid || order.State is not (OrderState.Open or OrderState.Completed) ||
            !await (from a in db.Allocations join b in db.BankTransactions on a.BankTransactionId equals b.Id where a.OrderId == order.Id && a.Primary && !b.Voided select a.Id).AnyAsync(cancellationToken)) return new(false, entitlementId);
        var entitlement = await db.Entitlements.AsNoTracking().SingleAsync(x => x.Id == entitlementId, cancellationToken);
        if (entitlement.State != EntitlementState.Active || entitlement.DigitalKind != DigitalDeliveryKind.InventoryItem) return new(false, entitlementId);
        var line = await db.OrderLines.AsNoTracking().SingleAsync(x => x.Id == entitlement.OrderLineId, cancellationToken);
        var purchased = JsonSerializer.Deserialize<PurchaseLine>(line.SnapshotJson, CatalogService.Json) ?? throw new InvalidOperationException("Missing purchase metadata.");
        var preview = JsonSerializer.Deserialize<ProductPreview>(purchased.PreviewJson, CatalogService.Json) ?? throw new InvalidOperationException("Missing model metadata.");
        var expiry = (await db.Sessions.SingleAsync(x => x.Id == sessionId, cancellationToken)).ExpiresAt;
        var lease = clock.GetUtcNow().AddSeconds(15); if (lease > expiry) lease = expiry;
        return new(true, entitlement.Id, shopperId, sessionId, entitlement.ProductId, entitlement.VariantId, preview.ModelKey, entitlement.UnitIndex, lease);
    }

    private async Task<ShopperSession> LockSessionAsync(Guid shopperId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = (await db.Sessions.FromSqlInterpolated($"SELECT * FROM ttb.\"Sessions\" WHERE \"Id\" = {sessionId} AND \"ShopperId\" = {shopperId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();
        return session is not null && session.RevokedAt is null && session.ExpiresAt > clock.GetUtcNow() ? session : throw new CommerceException(401, "game_session_expired", "Sign in again before using a game item.");
    }
    private void Reset() { if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Game authorization must own its transaction."); db.ChangeTracker.Clear(); }
    public void AssertProfile(string serverId)
    {
        if (options.GameServerKey.Length < 32 || string.IsNullOrEmpty(options.GameServerId)) throw new CommerceException(503, "game_authentication_unavailable", "Game service authentication is not configured.");
        if (serverId != options.GameServerId) throw new CommerceException(401, "game_server_invalid", "The game server is not registered.");
    }
    private static void ValidateChallenge(string challenge) { if (challenge is null || !Regex.IsMatch(challenge, "^[a-fA-F0-9]{32,128}$", RegexOptions.CultureInvariant)) throw InvalidTicket(); }
    private static CommerceException InvalidTicket() => new(401, "game_ticket_invalid", "Request a new authorization from the game connection.");
}
