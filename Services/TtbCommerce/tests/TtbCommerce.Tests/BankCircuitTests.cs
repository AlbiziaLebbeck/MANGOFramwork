using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class BankCircuitTests
{
    private static BankCircuitBreaker Circuit(MutableClock clock) => new(new() { FailureThreshold = 2, BreakSeconds = 30 }, clock);
    private static void Open(BankCircuitBreaker circuit, BankOperation operation)
    {
        for (var index = 0; index < 2; index++) circuit.Complete(Assert.IsType<BankCircuitBreaker.Permit>(circuit.TryEnter(operation)), true);
    }
    [Fact]
    public void A_failed_money_service_never_blocks_the_separate_void_enquiry_or_inbound_payment_path()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow); var circuit = Circuit(clock);
        Open(circuit, BankOperation.MerchantVoid);
        Assert.Null(circuit.TryEnter(BankOperation.MerchantVoid));
        Assert.Equal("bank_circuit_open", Assert.Throws<CommerceException>(() => circuit.AssertAvailable(BankOperation.MerchantVoid)).Code);
        foreach (var operation in new[] { BankOperation.Verify, BankOperation.EnquiryRange, BankOperation.EnquiryVoid })
        {
            circuit.AssertAvailable(operation);
            circuit.Complete(Assert.IsType<BankCircuitBreaker.Permit>(circuit.TryEnter(operation)), false);
        }
        Assert.Single(circuit.Read(), state => state.Open);
        Assert.Equal(4, circuit.Read().Select(state => BankCircuitBreaker.ServiceName(state.Operation)).Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.TryEnter((BankOperation)999));
    }
    [Fact]
    public async Task Only_one_explicit_trial_is_admitted_and_an_old_response_cannot_close_a_newer_failure_generation()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow); var circuit = Circuit(clock);
        var lateSuccess = Assert.IsType<BankCircuitBreaker.Permit>(circuit.TryEnter(BankOperation.Verify));
        Open(circuit, BankOperation.Verify); circuit.Complete(lateSuccess, false);
        Assert.True(circuit.Read().Single(state => state.Operation == BankOperation.Verify).Open);
        clock.Advance(TimeSpan.FromSeconds(31));
        var entries = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => circuit.TryEnter(BankOperation.Verify))));
        var trial = Assert.Single(entries.OfType<BankCircuitBreaker.Permit>());
        circuit.Abandon(trial); Assert.Null(circuit.TryEnter(BankOperation.Verify));
        clock.Advance(TimeSpan.FromSeconds(31));
        var recovered = Assert.IsType<BankCircuitBreaker.Permit>(circuit.TryEnter(BankOperation.Verify)); circuit.Complete(recovered, false); circuit.Abandon(recovered);
        Assert.False(circuit.Read().Single(state => state.Operation == BankOperation.Verify).Open);
    }
    [Theory]
    [InlineData(1, 30)] [InlineData(21, 30)] [InlineData(2, 0)] [InlineData(2, 301)]
    public void Invalid_resilience_limits_cannot_silently_disable_or_indefinitely_open_the_breaker(int failures, int seconds) =>
        Assert.Throws<InvalidOperationException>(() => new BankCircuitBreaker(new() { FailureThreshold = failures, BreakSeconds = seconds }, TimeProvider.System));

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.ServiceUnavailable;
        public Func<HttpContent>? ContentFactory;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Interlocked.Increment(ref Calls);
            var body = BankReadTests.Response(); body["ResultCode"] = "9999";
            return Task.FromResult(new HttpResponseMessage(Status) { Content = ContentFactory?.Invoke() ?? new StringContent(body.ToJsonString()) });
        }
    }
    private sealed class GatewayFixture : IDisposable
    {
        private readonly TestPrivateStorage storage = new();
        private readonly HttpClient client;
        public Handler Handler { get; } = new();
        public MutableClock Clock { get; } = new(DateTimeOffset.UtcNow);
        public BankCircuitBreaker Breaker { get; }
        public TtbBankGateway Gateway { get; }
        public GatewayFixture(TimeSpan? timeout = null)
        {
            Directory.CreateDirectory(storage.Root); var path = Path.Combine(storage.Root, "synthetic.pem");
            using var rsa = RSA.Create(2048); File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem(), Encoding.ASCII);
            var options = PaymentFixture.Bank; options.PrivateKeyPath = path; options.Issuer = "synthetic-circuit-test";
            Breaker = Circuit(Clock); client = new(Handler); if (timeout is not null) client.Timeout = timeout.Value;
            Gateway = new(client, PaymentFixture.Commerce, options, new(PaymentFixture.Commerce, options), new(options, Clock), Clock, Breaker);
        }
        public void Dispose() { client.Dispose(); storage.Dispose(); }
    }
    private sealed class StalledBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
    }
    [Fact]
    public async Task A_stalled_body_after_successful_HTTP_headers_has_a_whole_request_deadline_and_never_strands_the_circuit()
    {
        using var test = new GatewayFixture(TimeSpan.FromMilliseconds(100)); test.Handler.Status = HttpStatusCode.OK;
        test.Handler.ContentFactory = () => new StreamContent(new StalledBody());
        using var outer = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        for (var index = 0; index < 2; index++)
            Assert.Equal("Timeout", (await test.Gateway.PostAsync(BankOperation.Verify, new { }, Guid.NewGuid(), outer.Token)).TransportError);
        Assert.Equal(2, test.Handler.Calls); Assert.Null(test.Breaker.TryEnter(BankOperation.Verify));
        Assert.Equal("CircuitOpen", (await test.Gateway.PostAsync(BankOperation.Verify, new { }, Guid.NewGuid(), default)).TransportError);
        Assert.Equal(2, test.Handler.Calls);
    }
    [Fact]
    public async Task Actual_HTTP_gateway_suppresses_open_requests_without_retries_and_transport_recovery_never_implies_a_paid_result()
    {
        using var test = new GatewayFixture();
        for (var index = 0; index < 2; index++) Assert.Equal(503, (await test.Gateway.PostAsync(BankOperation.Verify, new { }, Guid.NewGuid(), default)).StatusCode);
        Assert.Equal(2, test.Handler.Calls);
        Assert.Equal("CircuitOpen", (await test.Gateway.PostAsync(BankOperation.Verify, new { }, Guid.NewGuid(), default)).TransportError);
        Assert.Equal(2, test.Handler.Calls); Assert.Throws<CommerceException>(() => test.Gateway.EnsureAvailable(BankOperation.Verify));
        await test.Gateway.PostAsync(BankOperation.EnquiryVoid, new { }, Guid.NewGuid(), default); Assert.Equal(3, test.Handler.Calls);
        test.Clock.Advance(TimeSpan.FromSeconds(31)); test.Handler.Status = HttpStatusCode.OK;
        var recovered = await test.Gateway.PostAsync(BankOperation.Verify, new { }, Guid.NewGuid(), default);
        Assert.Equal(4, test.Handler.Calls); Assert.False(test.Breaker.Read().Single(state => state.Operation == BankOperation.Verify).Open);
        Assert.Equal("Unconfirmed", BankReadResult.Parse(BankOperation.Verify, recovered, BankReadTests.Biller, BankReadTests.Qr).Outcome);
    }
    [Fact]
    public async Task Caller_cancellation_cannot_strand_the_single_trial_or_trigger_an_automatic_bank_retry()
    {
        using var test = new GatewayFixture(); Open(test.Breaker, BankOperation.MerchantVoid); test.Clock.Advance(TimeSpan.FromSeconds(31));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.Gateway.PostAsync(BankOperation.MerchantVoid, new { }, Guid.NewGuid(), cancelled.Token));
        Assert.Equal(0, test.Handler.Calls); Assert.Null(test.Breaker.TryEnter(BankOperation.MerchantVoid));
        test.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.NotNull(test.Breaker.TryEnter(BankOperation.MerchantVoid)); Assert.Equal(0, test.Handler.Calls);
    }
    private sealed class GuardedGateway(BankCircuitBreaker circuit) : IBankGateway
    {
        public int Calls;
        public void EnsureAvailable() { }
        public void EnsureAvailable(BankOperation operation) => circuit.AssertAvailable(operation);
        public Task<BankReply> PostAsync(BankOperation operation, object body, Guid requestUid, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new BankReply(null, [], "Unreachable")); }
    }
    [PgFact]
    public async Task An_open_verify_service_consumes_no_attempt_budget_and_does_not_block_the_separate_enquiry()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(61)); var circuit = Circuit(clock); Open(circuit, BankOperation.Verify);
        var gateway = new GuardedGateway(circuit); await using var db = database.Open();
        var service = new VerificationService(db, PaymentFixture.Commerce, PaymentFixture.Bank, gateway, PaymentFixture.Processor(db, clock), clock);
        Assert.Equal("bank_circuit_open", (await Assert.ThrowsAsync<CommerceException>(() => service.CheckAsync(fixture.ShopperId, fixture.OrderId, new(), default))).Code);
        Assert.Equal(0, gateway.Calls); Assert.Equal(0, await db.VerificationRequests.CountAsync()); Assert.Equal(0, (await db.Attempts.SingleAsync()).VerificationCount);
        await service.CheckAsync(fixture.ShopperId, fixture.OrderId, new("enquiry"), default);
        Assert.Equal(1, gateway.Calls); Assert.Equal(1, await db.VerificationRequests.CountAsync()); Assert.Equal(1, (await db.Attempts.SingleAsync()).VerificationCount);
        Assert.Equal(0, await db.Allocations.CountAsync()); Assert.Equal(PaymentState.Unpaid, (await db.Orders.SingleAsync()).PaymentState);
    }
    [PgFact]
    public async Task An_open_void_service_creates_no_financial_intent_or_send_fence_and_keeps_the_paid_order_unchanged()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, _) = await ReportFixtures.PaidAsync(database);
        var paidAt = new DateTimeOffset(2026, 10, 18, 3, 0, 0, TimeSpan.Zero); var clock = new MutableClock(paidAt.AddMinutes(1));
        await using var db = database.Open();
        await db.BankTransactions.ExecuteUpdateAsync(s => s.SetProperty(x => x.PaymentOccurredAt, paidAt).SetProperty(x => x.PaymentTimeSource, "ReportIsoPayment").SetProperty(x => x.CreatedAt, paidAt.AddSeconds(5)));
        await db.Attempts.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, paidAt.AddMinutes(-1)));
        var beforeJobs = await db.Jobs.CountAsync(); var circuit = Circuit(clock); Open(circuit, BankOperation.MerchantVoid); var gateway = new GuardedGateway(circuit);
        var policy = new VoidOptions { Enabled = true, ProfileApproved = true, WindowPolicy = VoidWindowPolicy.SameCalendarDayBefore2256 };
        var service = new VoidService(db, PaymentFixture.Commerce, PaymentFixture.Bank, policy, gateway, new(db, clock), clock);
        var finance = new ShopperActor(Guid.NewGuid(), "mangosgo", "circuit-finance", "Synthetic finance", ["finance"]);
        Assert.Equal("bank_circuit_open", (await Assert.ThrowsAsync<CommerceException>(() => service.RequestAsync(fixture.OrderId, new(Guid.NewGuid().ToString("D"), "Synthetic outage check"), finance, default))).Code);
        Assert.Equal(0, gateway.Calls); Assert.Equal(0, await db.Voids.CountAsync()); Assert.Equal(0, await db.VoidRequests.CountAsync());
        Assert.Equal(beforeJobs, await db.Jobs.CountAsync()); Assert.Equal(PaymentState.Paid, (await db.Orders.SingleAsync()).PaymentState);
        Assert.False(await db.Sequences.AnyAsync(sequence => sequence.Scope.StartsWith("void-id:")));
    }
}
