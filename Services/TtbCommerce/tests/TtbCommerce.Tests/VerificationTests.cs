using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class RecordingBankGateway : IBankGateway
{
    public ConcurrentQueue<(BankOperation Operation, JsonElement Body, Guid Uid)> Requests { get; } = new();
    public Func<BankOperation, JsonElement, CancellationToken, Task<BankReply>>? Handler { get; set; }
    public void EnsureAvailable() { }
    public Task<BankReply> PostAsync(BankOperation operation, object body, Guid requestUid, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToElement(body, BankWire.Json); Requests.Enqueue((operation, json, requestUid));
        return Handler?.Invoke(operation, json, cancellationToken) ?? Task.FromResult(NoData(operation, json));
    }
    public static BankReply NoData(BankOperation operation, JsonElement body) => new(200, JsonSerializer.SerializeToUtf8Bytes(new
    {
        ResCode = operation == BankOperation.Verify ? "0002" : "0001", ResDesc = "No data", BillerNo = body.GetProperty("BillerNo").GetString(),
        QRId = body.GetProperty("QRId").GetString(), TransDate = body.GetProperty("TransDate").GetString()
    }));
}

public sealed class VerificationTests
{
    private static VerificationService Service(CommerceDb db, MutableClock clock, RecordingBankGateway gateway, CommerceOptions? options = null) =>
        new(db, options ?? PaymentFixture.Commerce, PaymentFixture.Bank, gateway, PaymentFixture.Processor(db, clock), clock);

    [PgFact]
    public async Task Verify_and_enquiry_share_three_requests_and_cooldown_with_no_automatic_retries()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(61)); var gateway = new RecordingBankGateway();
        await using (var db = database.Open()) Assert.Equal("NotFound", (await Service(db, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new(), default)).Outcome);
        await using (var db = database.Open()) Assert.Equal("verification_cooldown", (await Assert.ThrowsAsync<CommerceException>(() => Service(db, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new("enquiry"), default))).Code);
        clock.Advance(TimeSpan.FromSeconds(31));
        await using (var db = database.Open()) Assert.Equal(2, (await Service(db, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new("enquiry"), default)).ChecksUsed);
        clock.Advance(TimeSpan.FromSeconds(31));
        await using (var db = database.Open()) Assert.Equal(0, (await Service(db, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new(), default)).ChecksRemaining);
        clock.Advance(TimeSpan.FromSeconds(31));
        await using (var db = database.Open()) Assert.Equal("verification_budget_exhausted", (await Assert.ThrowsAsync<CommerceException>(() => Service(db, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new("enquiry"), default))).Code);
        Assert.Equal(3, gateway.Requests.Count); Assert.Equal(3, gateway.Requests.Select(x => x.Uid).Distinct().Count());
        await using var check = database.Open(); Assert.Equal(3, await check.VerificationRequests.CountAsync()); Assert.Equal(3, await check.Observations.CountAsync()); Assert.Equal(0, await check.BankTransactions.CountAsync());
    }

    [PgFact]
    public async Task Ownership_and_initial_delay_are_checked_before_any_bank_request()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(10)); var gateway = new RecordingBankGateway();
        await using (var db = database.Open()) Assert.Equal(404, (await Assert.ThrowsAsync<CommerceException>(() => Service(db, clock, gateway).CheckAsync(Guid.NewGuid(), fixture.OrderId, new(), default))).Status);
        await using (var db = database.Open()) Assert.Equal("verification_wait", (await Assert.ThrowsAsync<CommerceException>(() => Service(db, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new(), default))).Code);
        Assert.Empty(gateway.Requests); await using var check = database.Open(); Assert.Equal(0, await check.VerificationRequests.CountAsync());
    }

    [PgFact]
    public async Task A_second_tab_cannot_start_enquiry_while_verify_is_in_flight()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(61));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new RecordingBankGateway { Handler = async (operation, body, cancellationToken) => { entered.SetResult(); await release.Task.WaitAsync(cancellationToken); return RecordingBankGateway.NoData(operation, body); } };
        await using var firstDb = database.Open(); var first = Service(firstDb, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new(), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await using var secondDb = database.Open();
            Assert.Equal("verification_in_progress", (await Assert.ThrowsAsync<CommerceException>(() => Service(secondDb, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new("enquiry"), default))).Code);
        }
        finally { release.SetResult(); }
        await first; Assert.Single(gateway.Requests);
    }

    [PgFact]
    public async Task Merchant_rate_limit_is_shared_by_orders_and_does_not_consume_a_rejected_attempt()
    {
        await using var database = await PgDatabase.CreateAsync(); var a = await PaymentFixture.CreateAsync(database); var b = await PaymentFixture.CreateAsync(database);
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(61)); var gateway = new RecordingBankGateway(); var options = PaymentFixture.Commerce; options.BankReadsPerMinute = 1;
        await using (var db = database.Open()) await Service(db, clock, gateway, options).CheckAsync(a.ShopperId, a.OrderId, new(), default);
        await using (var db = database.Open()) Assert.Equal("bank_rate_limited", (await Assert.ThrowsAsync<CommerceException>(() => Service(db, clock, gateway, options).CheckAsync(b.ShopperId, b.OrderId, new(), default))).Code);
        Assert.Single(gateway.Requests);
        await using var check = database.Open(); Assert.Equal(0, (await check.Attempts.SingleAsync(x => x.OrderId == b.OrderId)).VerificationCount);
    }

    [PgFact]
    public async Task Timeout_is_unconfirmed_and_consumes_one_check_without_retry()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(61)); var gateway = new RecordingBankGateway { Handler = (_, _, _) => Task.FromResult(new BankReply(null, [], "Timeout")) };
        await using (var db = database.Open())
        {
            var result = await Service(db, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new(), default);
            Assert.Equal("Unconfirmed", result.Outcome); Assert.Equal(1, result.ChecksUsed);
        }
        Assert.Single(gateway.Requests); await using var check = database.Open(); Assert.Equal(1, await check.Cases.CountAsync()); Assert.Equal(0, await check.Allocations.CountAsync()); Assert.Null((await check.Attempts.SingleAsync()).VerificationLeaseToken);
    }

    [PgFact]
    public async Task Cancelling_a_bank_query_does_not_reset_its_consumed_budget()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(61)); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new RecordingBankGateway { Handler = async (_, _, token) => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return new(null, [], "never"); } };
        using var cancellation = new CancellationTokenSource(); await using var db = database.Open();
        var task = Service(db, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await using var check = database.Open(); Assert.Equal(1, (await check.Attempts.SingleAsync()).VerificationCount); Assert.Null((await check.Attempts.SingleAsync()).VerificationLeaseToken);
        Assert.Equal("Unknown", (await check.VerificationRequests.SingleAsync()).Outcome); Assert.Single(gateway.Requests);
    }

    [PgFact]
    public async Task Callback_during_verify_uses_fresh_locked_state_and_does_not_overwrite_paid_money()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(61));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = JsonNode.Parse(fixture.Raw)!.AsObject(); reply["ResCode"] = "0000"; reply["ResDesc"] = "Successful"; reply["ResultCode"] = "0000";
        var gateway = new RecordingBankGateway { Handler = async (_, _, cancellationToken) => { entered.SetResult(); await release.Task.WaitAsync(cancellationToken); return BankReadTests.Reply(reply); } };
        await using var verifyDb = database.Open(); var task = Service(verifyDb, clock, gateway).CheckAsync(fixture.ShopperId, fixture.OrderId, new(), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await using var callbackDb = database.Open();
            await PaymentFixture.Processor(callbackDb, clock).NoticeAsync(JsonSerializer.SerializeToUtf8Bytes(fixture.Notice with { InstructionId = "CALLBACK-FIRST" }, BankWire.Json), PaymentFixture.Metadata(), default);
        }
        finally { release.SetResult(); }
        Assert.Equal("Review", (await task).Outcome);
        await using var check = database.Open(); Assert.Equal(PaymentState.Paid, (await check.Orders.SingleAsync()).PaymentState); Assert.Equal(OrderState.Review, (await check.Orders.SingleAsync()).State);
        Assert.Equal(1, await check.Allocations.CountAsync()); Assert.Equal(2, await check.BankTransactions.CountAsync());
    }
}
