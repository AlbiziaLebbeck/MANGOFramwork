namespace TtbCommerce.Core;

public sealed class GameUseTicket : Entity
{
    public Guid ShopperId { get; set; }
    public Guid SessionId { get; set; }
    public Guid EntitlementId { get; set; }
    public string ServerId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string ChallengeHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
public sealed record GameTicketInput(string ServerId, string Challenge, Guid EntitlementId);
public sealed record GameTicketView(string Ticket, DateTimeOffset ExpiresAt)
{
    public override string ToString() => $"GameTicketView {{ Ticket = [redacted], ExpiresAt = {ExpiresAt:O} }}";
}
public sealed record GameTicketConsume(string Ticket, string ServerId, string Challenge, Guid EntitlementId);
public sealed record GameLeaseInput(string ServerId, Guid ShopperId, Guid SessionId, Guid EntitlementId);
public sealed record GameGrant(bool Owned, Guid EntitlementId, Guid? ShopperId = null, Guid? SessionId = null, Guid? ProductId = null,
    Guid? VariantId = null, string? ModelKey = null, int? UnitIndex = null, DateTimeOffset? ValidUntil = null);
