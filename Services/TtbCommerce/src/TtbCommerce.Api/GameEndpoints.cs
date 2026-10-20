using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public static class GameServiceAccess
{
    public static void Require(HttpContext http, PrivateStorageOptions storage)
    {
        if (storage.GameServerKey.Length < 32) throw new CommerceException(503, "game_authentication_unavailable", "Game service authentication is not configured.");
        var authorization = http.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || !SecretTokens.Matches(authorization[7..], SecretTokens.Hash(storage.GameServerKey)))
            throw new CommerceException(401, "game_authentication_required", "A trusted game service is required.");
    }
}
public static class GameEndpoints
{
    public static void MapGameEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapPost("/session/game-ticket", async (GameTicketInput input, HttpContext http, ShopperAccess access, GameTicketService tickets) =>
        {
            var actor = await access.RequireAsync(http, mutation: true); var session = (await access.ResolveAsync(http))!;
            return Results.Ok(await tickets.IssueAsync(actor.ShopperId, session.Session.Id, input, http.RequestAborted));
        });
        routes.MapPost("/game/tickets/consume", async (GameTicketConsume input, HttpContext http, PrivateStorageOptions storage, GameTicketService tickets) =>
        {
            GameServiceAccess.Require(http, storage); return Results.Ok(await tickets.ConsumeAsync(input, http.RequestAborted));
        });
        routes.MapPost("/game/entitlements/lease", async (GameLeaseInput input, HttpContext http, PrivateStorageOptions storage, GameTicketService tickets) =>
        {
            GameServiceAccess.Require(http, storage); return Results.Ok(await tickets.LeaseAsync(input, http.RequestAborted));
        });
    }
}
