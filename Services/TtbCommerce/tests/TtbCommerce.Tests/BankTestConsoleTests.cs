using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class BankTestConsoleTests : IDisposable
{
    private readonly string key = Path.Combine(Path.GetTempPath(), "bank-test-only-" + Guid.NewGuid().ToString("N") + ".pem");
    private readonly CommerceOptions commerce = PaymentFixture.Commerce;
    private readonly BankTestOptions options = new() { Enabled = true, VoidEnabled = true };
    private readonly TtbOptions bank;
    private readonly MutableClock clock = new(DateTimeOffset.Parse("2026-10-21T10:00:00+07:00"));
    public BankTestConsoleTests()
    {
        using var rsa = RSA.Create(2048); File.WriteAllText(key, rsa.ExportPkcs8PrivateKeyPem());
        bank = PaymentFixture.Bank; bank.Issuer = "unit"; bank.PrivateKeyPath = key; bank.QrProfileApproved = true;
        bank.QrMerchantName = "UNIT TEST"; bank.BeneficiaryName = "บริษัท สร้างสรรค์ปัญญา จำกัด";
    }
    public void Dispose() => File.Delete(key);
    private BankTestConsoleService Service(CommerceDb db, IBankGateway? gateway = null) =>
        new(db, commerce, bank, options, new(db, clock), new(commerce, bank), gateway ?? new TestGateway(), clock,
            NullLogger<BankTestConsoleService>.Instance);
    private static byte[] Notice(BankTestCaseView row, string instruction = "TEST-INSTRUCTION") =>
        JsonSerializer.SerializeToUtf8Bytes(new CreditNotice(instruction, "111111111111111", row.Ref1, row.Ref2, row.QrId,
            new Money(row.AmountMinor).ToString(), "000", "Successful", "20261021100005", "1234567890", "PRIVATE PAYER", "0001"), BankWire.Json);
    private static async Task EmptyLedger(CommerceDb db)
    {
        Assert.Equal(0, await db.Orders.CountAsync()); Assert.Equal(0, await db.Attempts.CountAsync());
        Assert.Equal(0, await db.BankTransactions.CountAsync()); Assert.Equal(0, await db.Allocations.CountAsync());
        Assert.Equal(0, await db.Observations.CountAsync()); Assert.Equal(0, await db.CallbackReceipts.CountAsync());
        Assert.Equal(0, await db.Fulfillments.CountAsync()); Assert.Equal(0, await db.Entitlements.CountAsync());
        Assert.Equal(0, await db.Documents.CountAsync()); Assert.Equal(0, await db.Jobs.CountAsync()); Assert.Equal(0, await db.Voids.CountAsync());
    }

    [PgFact]
    public async Task Reserved_notification_is_durable_deduplicated_redacted_and_never_creates_shopper_financial_records()
    {
        await using var database = await PgDatabase.CreateAsync(); BankTestCaseView test;
        await using (var db = database.Open()) test = await Service(db).CreateAsync(new("1.00"), default);
        Assert.Equal("TEST", test.Ref1); Assert.Equal(bank.BeneficiaryName, test.BeneficiaryName); Assert.StartsWith("data:image/png", test.ImageDataUrl);
        var raw = Notice(test);
        var replies = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = database.Open(); return await Service(db).NotificationAsync(raw, PaymentFixture.Metadata(), true, default);
        }));
        Assert.Single(replies, x => x!.ResCode == "0000"); Assert.Equal(5, replies.Count(x => x!.ResCode == "0004"));
        await using var check = database.Open(); await EmptyLedger(check);
        Assert.Equal(6, (await check.BankTestReceipts.SingleAsync()).DeliveryCount);
        Assert.Equal("Paid", (await check.BankTestCases.SingleAsync()).State);
        var events = await check.BankTestEvents.Where(x => x.Kind == "Notification").ToArrayAsync(); Assert.Equal(6, events.Length);
        Assert.All(events, e => { Assert.Equal(raw, e.RawPayload); Assert.DoesNotContain("PRIVATE PAYER", e.PayloadJson); Assert.DoesNotContain("1234567890", e.PayloadJson); });
        Assert.Equal(7, (await Service(check).EventsAsync(null, null, null, 100, default)).Length);
        var disguised = BankWire.ParseNotice(raw) with { Ref1 = "NONTEST", ResultCode = "0000" };
        Assert.Equal("bank_test_isolated", (await Assert.ThrowsAsync<CommerceException>(() => PaymentFixture.Processor(check).ApplyAsync(disguised, raw, PaymentFixture.Metadata(EvidenceSource.Verify), default))).Code);
    }

    [PgFact]
    public async Task Known_test_QR_stays_isolated_when_headers_ref_or_money_are_wrong_and_external_TEST_is_observed_only()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var db = database.Open(); var service = Service(db);
        var test = await service.CreateAsync(new(), default);
        Assert.Equal("0001", (await service.NotificationAsync(Notice(test), PaymentFixture.Metadata(), false, default))!.ResCode);
        Assert.Equal("0006", (await service.NotificationAsync(Notice(test with { Ref1 = "WRONG" }, "WRONG-REF"), PaymentFixture.Metadata(), true, default))!.ResCode);
        Assert.Equal("0005", (await service.NotificationAsync(Notice(test with { AmountMinor = 200 }, "WRONG-AMOUNT"), PaymentFixture.Metadata(), true, default))!.ResCode);
        var external = test with { QrId = "ZTSTE261021101000099", Ref2 = "BANK-TEST" };
        Assert.Equal("0000", (await service.NotificationAsync(Notice(external, "EXTERNAL"), PaymentFixture.Metadata(), true, default))!.ResCode);
        var observed = await db.BankTestCases.SingleAsync(x => x.QrId == external.QrId);
        Assert.Equal("Observed", observed.State); Assert.Equal("BankCallback", observed.Origin); Assert.Null(observed.ExpiresAt);
        var error = await Assert.ThrowsAsync<CommerceException>(() => service.ActionAsync(observed.Id, "void", new(Guid.NewGuid().ToString("D"), "TEST"), default));
        Assert.Equal("bank_test_void_unproven", error.Code); await EmptyLedger(db);
        options.Enabled = false;
        Assert.Equal("bank_test_unavailable", (await Assert.ThrowsAsync<CommerceException>(() => service.NotificationAsync(Notice(test), PaymentFixture.Metadata(), true, default))).Code);
    }

    [PgFact]
    public async Task Customer_QR_or_instruction_cannot_be_targeted_and_normal_callbacks_still_pay_normally()
    {
        await using var database = await PgDatabase.CreateAsync(); var shopper = await PaymentFixture.CreateAsync(database);
        await using var db = database.Open(); var service = Service(db);
        Assert.Null(await service.NotificationAsync(shopper.Raw, PaymentFixture.Metadata(), true, default));
        var forged = JsonSerializer.SerializeToUtf8Bytes(shopper.Notice with { Ref1 = "TEST", InstructionId = "FORGED-CUSTOMER-QR" }, BankWire.Json);
        Assert.Equal("0006", (await service.NotificationAsync(forged, PaymentFixture.Metadata(), true, default))!.ResCode);
        Assert.Empty(await db.BankTestCases.ToArrayAsync()); Assert.Equal(PaymentState.Unpaid, (await db.Orders.SingleAsync()).PaymentState);
        Assert.Equal("0000", (await PaymentFixture.Processor(db).NoticeAsync(shopper.Raw, PaymentFixture.Metadata(), default)).Ack.ResCode);
        var test = await service.CreateAsync(new(), default);
        Assert.Equal("0006", (await service.NotificationAsync(Notice(test, shopper.Notice.InstructionId!), PaymentFixture.Metadata(), true, default))!.ResCode);
        Assert.Equal(PaymentState.Paid, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState);
        Assert.Equal(1, await db.BankTransactions.CountAsync()); Assert.Equal(1, await db.Allocations.CountAsync());
        var evidence = BankWire.ParseNotice(Notice(test)) with { ResultCode = "0000" };
        Assert.Equal("bank_test_isolated", (await Assert.ThrowsAsync<CommerceException>(() => PaymentFixture.Processor(db).ApplyAsync(evidence, Notice(test), PaymentFixture.Metadata(EvidenceSource.Verify), default))).Code);
    }

    [PgFact]
    public async Task Verify_wait_budget_idempotency_and_validated_matching_bank_evidence_affect_only_the_test_case()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var db = database.Open(); var gateway = new TestGateway(database); var service = Service(db, gateway);
        var test = await service.CreateAsync(new(), default); var input = new BankTestActionInput(Guid.NewGuid().ToString("D"));
        Assert.Equal("verification_wait", (await Assert.ThrowsAsync<CommerceException>(() => service.ActionAsync(test.Id, "verify", input, default))).Code);
        Assert.Equal(0, gateway.Sends); clock.Advance(TimeSpan.FromSeconds(61));
        gateway.Credit = Notice(test); var result = await service.ActionAsync(test.Id, "verify", input, default);
        Assert.Equal("Found", result.Outcome); Assert.Equal("Paid", (await service.CaseAsync(test.Id, default)).State);
        Assert.Equal(result.Id, (await service.ActionAsync(test.Id, "verify", input, default)).Id); Assert.Equal(1, gateway.Sends);
        Assert.Equal("bank_test_cooldown", (await Assert.ThrowsAsync<CommerceException>(() => service.ActionAsync(test.Id, "verify", new(Guid.NewGuid().ToString("D")), default))).Code);
        clock.Advance(TimeSpan.FromSeconds(31)); options.ReadBudget = 1;
        Assert.Equal("bank_test_read_budget", (await Assert.ThrowsAsync<CommerceException>(() => service.ActionAsync(test.Id, "verify", new(Guid.NewGuid().ToString("D")), default))).Code);
        await EmptyLedger(db);
    }

    [PgFact]
    public async Task Void_is_fenced_before_send_and_timeout_cannot_be_retried_but_enquiry_uses_original_identity()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var db = database.Open(); var gateway = new TestGateway(database) { LoseVoid = true }; var service = Service(db, gateway);
        var test = await service.CreateAsync(new(), default);
        await service.NotificationAsync(Notice(test), PaymentFixture.Metadata(), true, default); clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("bank_test_void_confirmation", (await Assert.ThrowsAsync<CommerceException>(() => service.ActionAsync(test.Id, "void", new(Guid.NewGuid().ToString("D")), default))).Code);
        var input = new BankTestActionInput(Guid.NewGuid().ToString("D"), "TEST");
        var sent = await service.ActionAsync(test.Id, "void", input, default); Assert.Equal("Unknown", sent.Outcome);
        Assert.Equal("VoidUnknown", (await service.CaseAsync(test.Id, default)).State);
        Assert.Equal(sent.Id, (await service.ActionAsync(test.Id, "void", input, default)).Id); Assert.Equal(1, gateway.Sends);
        clock.Advance(TimeSpan.FromSeconds(31));
        await Assert.ThrowsAsync<CommerceException>(() => service.ActionAsync(test.Id, "void", new(Guid.NewGuid().ToString("D"), "TEST"), default)); Assert.Equal(1, gateway.Sends);
        var enquiry = await service.ActionAsync(test.Id, "enquiry-void", new(Guid.NewGuid().ToString("D")), default);
        Assert.Equal("Confirmed", enquiry.Outcome); Assert.Equal(sent.TransactionId, enquiry.TransactionId);
        Assert.Equal("Voided", (await service.CaseAsync(test.Id, default)).State); Assert.Equal(2, gateway.Sends); await EmptyLedger(db);
    }

    [PgFact]
    public async Task Cancel_after_void_fence_preserves_unknown_and_fresh_key_cannot_resend()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var db = database.Open(); var gateway = new TestGateway(database) { CancelVoid = true }; var service = Service(db, gateway);
        var test = await service.CreateAsync(new(), default); await service.NotificationAsync(Notice(test), PaymentFixture.Metadata(), true, default); clock.Advance(TimeSpan.FromSeconds(61));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ActionAsync(test.Id, "void", new(Guid.NewGuid().ToString("D"), "TEST"), default));
        var operation = await db.BankTestOperations.SingleAsync(); Assert.NotNull(operation.CompletedAt); Assert.Equal("Unknown", operation.Outcome);
        clock.Advance(TimeSpan.FromSeconds(61)); await Assert.ThrowsAsync<CommerceException>(() => service.ActionAsync(test.Id, "void", new(Guid.NewGuid().ToString("D"), "TEST"), default));
        Assert.Equal(1, gateway.Sends); await EmptyLedger(db);
    }

    [Fact]
    public void Production_and_invalid_marker_are_closed()
    {
        using var db = new CommerceDb(new DbContextOptionsBuilder<CommerceDb>().Options);
        Assert.True(options.ValidProfile()); options.Reference1 = "T20261021000001"; Assert.False(options.ValidProfile());
        options.Reference1 = "TEST"; commerce.Environment = CommerceEnvironment.Production;
        Assert.Equal("bank_test_unavailable", Assert.Throws<CommerceException>(() => Service(db).Guard()).Code);
    }

    private sealed class TestGateway(PgDatabase? database = null) : IBankGateway
    {
        public void EnsureAvailable() { }
        public int Sends; public bool LoseVoid; public bool CancelVoid; public byte[]? Credit;
        public async Task<BankReply> PostAsync(BankOperation operation, object body, Guid requestUid, CancellationToken ct)
        {
            Sends++; var json = JsonSerializer.SerializeToElement(body);
            if (database is not null)
            {
                await using var db = database.Open(); var fence = await db.BankTestOperations.SingleAsync(x => x.RequestUid == requestUid.ToString("D"), ct);
                Assert.NotEqual(default, fence.SendFencedAt); Assert.Null(fence.CompletedAt);
                Assert.True(await db.BankTestEvents.AnyAsync(x => x.OperationId == fence.Id, ct));
            }
            if (operation == BankOperation.Verify)
            {
                var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Credit!)!;
                fields["ResultCode"] = JsonSerializer.SerializeToElement("0000"); fields["ResCode"] = JsonSerializer.SerializeToElement("0000");
                return new(200, JsonSerializer.SerializeToUtf8Bytes(fields));
            }
            if (operation == BankOperation.MerchantVoid)
            {
                Assert.False(json.TryGetProperty("VoidAmount", out _));
                if (CancelVoid) throw new OperationCanceledException();
                if (LoseVoid) return new(null, [], "Timeout");
            }
            return VoidProtocolTests.Reply(new VoidOperation { TransactionId = json.GetProperty("TransactionId").GetString()!,
                InstructionId = json.GetProperty("InstructionId").GetString()!, QrId = json.GetProperty("QRId").GetString()!, BillerNo = json.GetProperty("BillerNo").GetString()! }, "0000");
        }
    }
}

public sealed class BankTestConsoleHttpTests
{
    private const string Root = "/internal/bank-test-console";
    private sealed class Factory(string connection, CommerceEnvironment environment = CommerceEnvironment.Uat) : CommerceApiFactory(connection, environment: environment)
    {
        public IPAddress Peer { get; set; } = IPAddress.Loopback;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["AllowedHosts"] = "*", // Exercise the console host guard even without the outer ASP.NET host filter.
              ["Receipts:CompanyProfileApproved"] = "true", ["Receipts:TaxId"] = "1111111111111", // Synthetic profile for the isolated Production host test.
              ["BankTests:Enabled"] = "true", ["Ttb:CallbackAuthentication"] = "BankSourceIp", ["Ttb:CallbackProfileApproved"] = "true",
              ["Ttb:MerchantId"] = "TST", ["Ttb:BillerNo"] = "111111111111111", ["Ttb:TrustedProxyCidrs:0"] = "10.42.0.7/32" }));
            builder.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new PeerFilter(() => Peer)));
        }
        private sealed class PeerFilter(Func<IPAddress> peer) : IStartupFilter
        { public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.Use((c, n) => { c.Connection.RemoteIpAddress = peer(); return n(c); }); next(app); }; }
    }
    [PgFact]
    public async Task Console_requires_original_loopback_host_and_action_origin_and_is_absent_in_production()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var factory = new Factory(database.ConnectionString); using var client = factory.ShopperClient();
        client.DefaultRequestHeaders.Remove("Origin");
        using var page = await client.GetAsync(Root + "/"); Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("คอนโซลทดสอบ", await page.Content.ReadAsStringAsync()); Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Root + "/font.ttf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Root + "/api/cases", new BankTestCreateInput())).StatusCode);
        client.DefaultRequestHeaders.Add("X-Bank-Test-Action", "1"); client.DefaultRequestHeaders.Add("Origin", "https://evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Root + "/api/cases", new BankTestCreateInput())).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Host = "evil.test";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Root + "/api/cases")).StatusCode);
        client.DefaultRequestHeaders.Host = "localhost"; factory.Peer = IPAddress.Parse("10.42.0.7"); client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Root + "/api/cases")).StatusCode);
        factory.Peer = IPAddress.Parse("203.148.143.101");
        var notice = Encoding.UTF8.GetBytes("{\"InstructionId\":\"HTTP-TEST\",\"BillerNo\":\"111111111111111\",\"Ref1\":\"TEST\",\"Ref2\":\"BANK\",\"QRId\":\"ZTSTE261021101000099\",\"Amount\":\"1.00\",\"ResultCode\":\"000\",\"ResultDesc\":\"Successful\",\"TransDate\":\"20261021100005\"}");
        client.DefaultRequestHeaders.Remove("X-Forwarded-For"); using var request = new HttpRequestMessage(HttpMethod.Post, "/ttb/v1/credit-notification") { Content = new ByteArrayContent(notice) };
        request.Content.Headers.ContentType = new("application/json"); request.Headers.Add("Service-Name", "credit-notification"); request.Headers.Add("Request-Datetime", "2026-10-21T10:00:05+07:00");
        using var callback = await client.SendAsync(request); Assert.Equal("0000", (await callback.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ResCode").GetString());
        await using var check = database.Open(); Assert.Equal(1, await check.BankTestCases.CountAsync()); Assert.Equal(0, await check.BankTransactions.CountAsync());
        await using var productionDatabase = await PgDatabase.CreateAsync();
        await using var production = new Factory(productionDatabase.ConnectionString, CommerceEnvironment.Production); using var prod = production.ShopperClient();
        Assert.Equal(HttpStatusCode.NotFound, (await prod.GetAsync(Root + "/api/cases")).StatusCode);
    }
}
