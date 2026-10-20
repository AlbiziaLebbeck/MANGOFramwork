using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class GameTicketTests
{
    private const string Key = "unit-only-game-server-key-never-ship-this";
    private const string Server = "mangos-ttb";
    private const string Challenge = "1234567890abcdef1234567890abcdef";
    private static PrivateStorageOptions Options => new() { GameServerKey = Key, GameServerId = Server };
    private sealed record Fixture(Guid Shopper, Guid Session, Guid Entitlement, Guid Order);
    private static async Task<Fixture> CreateAsync(PgDatabase database, TestPrivateStorage storage)
    {
        await using var db = database.Open(); var actor = await CartTests.ShopperAsync(db, "alice");
        await db.Shoppers.Where(x => x.Id == actor.ShopperId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Issuer, "mangosgo"));
        var item = await CatalogTests.PublishedAsync(db, "game-star", ProductKind.Digital); var quote = await OrderTests.QuoteAsync(db, actor, (item.Variant.Id, 1));
        var order = await OrderTests.Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "game-buy", default);
        var options = new CommerceOptions(); var ttb = new TtbOptions();
        await new QrService(db, new(db, TimeProvider.System), options, ttb, new(options, ttb), TimeProvider.System).GetOrCreateAsync(actor.ShopperId, order.Id, default);
        await new MockBankService(db, options, new(db, options, ttb, TimeProvider.System), TimeProvider.System).PayAsync(actor.ShopperId, order.Id, new("game-payment"), default);
        var line = await db.OrderLines.SingleAsync(x => x.OrderId == order.Id);
        await new FulfillmentService(db, [new DigitalLineDelivery(db, storage.Files, TimeProvider.System)], TimeProvider.System).DispatchAsync(order.Id, line.Id, default);
        var session = new ShopperSession { ShopperId = actor.ShopperId, TokenHash = SecretTokens.Hash(SecretTokens.New()), CsrfHash = SecretTokens.Hash(SecretTokens.New()), IdentityFingerprint = "unit-only", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        db.Sessions.Add(session); await db.SaveChangesAsync();
        return new(actor.ShopperId, session.Id, await db.Entitlements.Select(x => x.Id).SingleAsync(), order.Id);
    }
    private static GameTicketInput Input(Fixture fixture) => new(Server, Challenge, fixture.Entitlement);
    private static GameTicketConsume Consume(Fixture fixture, string token) => new(token, Server, Challenge, fixture.Entitlement);

    [PgFact]
    public async Task Single_use_ticket_concurrent_replays_authorize_only_one_game_request_and_store_no_bearer()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage); GameTicketView ticket;
        await using (var db = database.Open()) ticket = await new GameTicketService(db, Options, TimeProvider.System).IssueAsync(fixture.Shopper, fixture.Session, Input(fixture), default);
        Assert.DoesNotContain(ticket.Ticket, ticket.ToString());
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = database.Open(); try { return (await new GameTicketService(db, Options, TimeProvider.System).ConsumeAsync(Consume(fixture, ticket.Ticket), default)).Owned; }
            catch (CommerceException e) when (e.Code == "game_ticket_invalid") { return false; }
        }));
        Assert.Single(results, x => x); await using var check = database.Open(); var stored = await check.GameTickets.SingleAsync();
        Assert.NotEqual(ticket.Ticket, stored.TokenHash); Assert.NotNull(stored.UsedAt); Assert.Equal(1, await check.AuditEvents.CountAsync(x => x.Action == "game.ticket.consume"));
        Assert.DoesNotContain(await check.AuditEvents.ToArrayAsync(), x => x.DataJson.Contains(ticket.Ticket, StringComparison.Ordinal));
    }

    [PgFact]
    public async Task Ticket_is_bound_to_shopper_session_game_server_nonce_and_exact_entitlement()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage);
        await using var db = database.Open(); var service = new GameTicketService(db, Options, TimeProvider.System);
        await Assert.ThrowsAsync<CommerceException>(() => service.IssueAsync(Guid.NewGuid(), fixture.Session, Input(fixture), default));
        var ticket = await service.IssueAsync(fixture.Shopper, fixture.Session, Input(fixture), default); var request = Consume(fixture, ticket.Ticket);
        foreach (var wrong in new[] { request with { ServerId = "other-world" }, request with { Challenge = new string('f', 32) }, request with { EntitlementId = Guid.NewGuid() } })
            Assert.Equal(401, (await Assert.ThrowsAsync<CommerceException>(() => service.ConsumeAsync(wrong, default))).Status);
        var grant = await service.ConsumeAsync(request, default); Assert.True(grant.Owned); Assert.Equal(fixture.Shopper, grant.ShopperId); Assert.Equal(fixture.Session, grant.SessionId); Assert.Equal("lamp", grant.ModelKey);
        Assert.True(grant.ValidUntil > DateTimeOffset.UtcNow); Assert.True(grant.ValidUntil <= DateTimeOffset.UtcNow.AddSeconds(16));
    }

    [PgFact]
    public async Task Expired_or_logged_out_session_tickets_cannot_authorize_game_items()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage);
        await using var db = database.Open(); var clock = new MutableClock(DateTimeOffset.UtcNow); var service = new GameTicketService(db, Options, clock);
        var ticket = await service.IssueAsync(fixture.Shopper, fixture.Session, Input(fixture), default); clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("game_ticket_invalid", (await Assert.ThrowsAsync<CommerceException>(() => service.ConsumeAsync(Consume(fixture, ticket.Ticket), default))).Code);
        var fresh = await service.IssueAsync(fixture.Shopper, fixture.Session, Input(fixture), default);
        await db.Sessions.Where(x => x.Id == fixture.Session).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()));
        Assert.Equal("game_session_expired", (await Assert.ThrowsAsync<CommerceException>(() => service.ConsumeAsync(Consume(fixture, fresh.Ticket), default))).Code);
    }

    [PgFact]
    public async Task Active_game_lease_stops_on_finance_hold_revocation_void_and_logout()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage);
        await using var db = database.Open(); var service = new GameTicketService(db, Options, TimeProvider.System); var request = new GameLeaseInput(Server, fixture.Shopper, fixture.Session, fixture.Entitlement);
        Assert.True((await service.LeaseAsync(request, default)).Owned);
        await db.Orders.Where(x => x.Id == fixture.Order).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, OrderState.Review)); Assert.False((await service.LeaseAsync(request, default)).Owned);
        await db.Orders.Where(x => x.Id == fixture.Order).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, OrderState.Completed));
        await db.Entitlements.Where(x => x.Id == fixture.Entitlement).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, EntitlementState.Revoked)); Assert.False((await service.LeaseAsync(request, default)).Owned);
        await db.Entitlements.Where(x => x.Id == fixture.Entitlement).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, EntitlementState.Active));
        await db.BankTransactions.ExecuteUpdateAsync(s => s.SetProperty(x => x.Voided, true)); Assert.False((await service.LeaseAsync(request, default)).Owned);
        await db.BankTransactions.ExecuteUpdateAsync(s => s.SetProperty(x => x.Voided, false));
        await db.Sessions.Where(x => x.Id == fixture.Session).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)); Assert.False((await service.LeaseAsync(request, default)).Owned);
    }

    [PgFact]
    public async Task Ticket_rate_limit_is_shopper_scoped_and_rolls_forward_without_extending_old_tickets()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage);
        await using var db = database.Open(); var clock = new MutableClock(DateTimeOffset.UtcNow);
        for (var i = 0; i < 20; i++) db.GameTickets.Add(new GameUseTicket { ShopperId = fixture.Shopper, SessionId = fixture.Session, EntitlementId = fixture.Entitlement,
            ServerId = Server, TokenHash = SecretTokens.Hash(SecretTokens.New()), ChallengeHash = SecretTokens.Hash(Challenge), ExpiresAt = clock.GetUtcNow().AddSeconds(60), CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(); var service = new GameTicketService(db, Options, clock);
        Assert.Equal(429, (await Assert.ThrowsAsync<CommerceException>(() => service.IssueAsync(fixture.Shopper, fixture.Session, Input(fixture), default))).Status);
        clock.Advance(TimeSpan.FromSeconds(61)); var ticket = await service.IssueAsync(fixture.Shopper, fixture.Session, Input(fixture), default); Assert.True(ticket.ExpiresAt > clock.GetUtcNow());
    }

    [PgFact]
    public async Task HTTP_ticket_requires_owned_session_CSRF_and_server_key_and_account_switch_revokes_lease()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var fixture = await CreateAsync(database, storage);
        await using var factory = new CommerceApiFactory(database.ConnectionString) { GameKey = Key, PrivateRoot = storage.Root };
        using var alice = factory.ShopperClient("alice"); using var bob = factory.ShopperClient("bob"); var login = await CartTests.SignInAsync(alice); await CartTests.SignInAsync(bob);
        var input = new { serverId = Server, challenge = Challenge, entitlementId = fixture.Entitlement, shopperId = fixture.Shopper, paid = true };
        alice.DefaultRequestHeaders.Remove("X-CSRF-Token"); Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsJsonAsync("/ttb-commerce/v1/session/game-ticket", input)).StatusCode);
        alice.DefaultRequestHeaders.Add("X-CSRF-Token", login.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync("/ttb-commerce/v1/session/game-ticket", input)).StatusCode);
        var issued = await alice.PostAsJsonAsync("/ttb-commerce/v1/session/game-ticket", input); issued.EnsureSuccessStatusCode(); var ticket = (await issued.Content.ReadFromJsonAsync<GameTicketView>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.PostAsJsonAsync("/ttb-commerce/v1/game/tickets/consume", Consume(fixture, ticket.Ticket))).StatusCode);
        using var game = factory.ShopperClient(); game.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        var result = await game.PostAsJsonAsync("/ttb-commerce/v1/game/tickets/consume", Consume(fixture, ticket.Ticket)); result.EnsureSuccessStatusCode(); var grant = (await result.Content.ReadFromJsonAsync<GameGrant>())!; Assert.True(grant.Owned);
        alice.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bob");
        Assert.False((await alice.GetFromJsonAsync<JsonElement>("/ttb-commerce/v1/session")).GetProperty("authenticated").GetBoolean());
        var lease = await game.PostAsJsonAsync("/ttb-commerce/v1/game/entitlements/lease", new GameLeaseInput(Server, grant.ShopperId!.Value, grant.SessionId!.Value, fixture.Entitlement)); lease.EnsureSuccessStatusCode(); Assert.False((await lease.Content.ReadFromJsonAsync<GameGrant>())!.Owned);
        await using var check = database.Open(); Assert.NotNull((await check.Sessions.SingleAsync(x => x.Id == grant.SessionId)).RevokedAt);
    }
}
