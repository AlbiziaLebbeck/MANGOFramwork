using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class VoidProtocolTests
{
    private static DateTimeOffset At(string value) => DateTimeOffset.Parse("2026-10-18T" + value + "+07:00", CultureInfo.InvariantCulture);
    [Fact]
    public void Unmatched_credit_never_uses_bank_system_time_as_original_payment_proof()
    {
        var options = new VoidOptions { WindowPolicy = VoidWindowPolicy.SameCalendarDayBefore2256 };
        var bank = new BankTransaction { InstructionId = "INSTR", CreatedAt = At("10:00:05"), BankTransDate = "20261018100005" };
        Assert.Equal("void_original_time_unproven", VoidWindow.Check(bank, null, options, At("10:01:00")).Code);
        bank.PaymentOccurredAt = At("10:00:00"); bank.PaymentTimeSource = "ReportLostCreditOperation";
        Assert.False(VoidWindow.Check(bank, null, options, At("10:01:00")).Eligible);
        bank.PaymentTimeSource = "ReportIsoPayment"; Assert.True(VoidWindow.Check(bank, null, options, At("10:01:00")).Eligible);
    }
    [Fact]
    public void Eligibility_uses_original_proof_or_the_complete_issue_to_observation_bound_never_system_TransDate()
    {
        var options = new VoidOptions { WindowPolicy = VoidWindowPolicy.SameCalendarDayBefore2256 };
        var attempt = new PaymentAttempt { CreatedAt = At("10:00:00") }; var bank = new BankTransaction { CreatedAt = At("10:00:05"), BankTransDate = "20261018220000", InstructionId = "INSTR" };
        Assert.True(VoidWindow.Check(bank, attempt, options, At("10:01:00")).Eligible);
        attempt.CreatedAt = At("00:00:01").AddDays(-1); Assert.False(VoidWindow.Check(bank, attempt, options, At("10:01:00")).Eligible);
        bank.PaymentOccurredAt = At("10:00:01"); bank.PaymentTimeSource = "ReportIsoPayment"; Assert.True(VoidWindow.Check(bank, attempt, options, At("10:01:00")).Eligible);
        bank.PaymentTimeSource = "SystemTimeOnly"; Assert.False(VoidWindow.Check(bank, attempt, options, At("10:01:00")).Eligible);
        bank.PaymentTimeSource = "ReportIsoPayment"; Assert.False(VoidWindow.Check(bank, attempt, options, At("22:55:30")).Eligible); Assert.False(VoidWindow.Check(bank, attempt, options, At("22:56:00")).Eligible);
        bank.CrossBorderFlag = "Y"; Assert.Equal("void_cross_border_unavailable", VoidWindow.Check(bank, attempt, options, At("10:01:00")).Code);
        bank.CrossBorderFlag = null; bank.InstructionId = "Q123"; Assert.Equal("void_instruction_ineligible", VoidWindow.Check(bank, attempt, options, At("10:01:00")).Code);
        bank.InstructionId = "INSTR"; options.WindowPolicy = VoidWindowPolicy.Unknown; Assert.False(VoidWindow.Check(bank, attempt, options, At("10:01:00")).Eligible);
    }

    [Fact]
    public void Approved_banking_window_and_conservative_calendar_window_are_explicitly_different_at_midnight()
    {
        var options = new VoidOptions { WindowPolicy = VoidWindowPolicy.BankingDay2256 };
        var bank = new BankTransaction { InstructionId = "INSTR", CreatedAt = At("23:10:00").AddDays(-1) }; var attempt = new PaymentAttempt { CreatedAt = At("23:00:00").AddDays(-1) };
        Assert.True(VoidWindow.Check(bank, attempt, options, At("00:01:00")).Eligible);
        options.WindowPolicy = VoidWindowPolicy.SameCalendarDayBefore2256; Assert.False(VoidWindow.Check(bank, attempt, options, At("00:01:00")).Eligible);
    }

    [Theory]
    [InlineData("0000", "Confirmed")]
    [InlineData("0002", "AlreadyVoided")]
    [InlineData("0001", "Unknown")]
    [InlineData("0003", "Rejected")]
    [InlineData("0004", "Rejected")]
    [InlineData("0005", "Unknown")]
    [InlineData("0008", "Rejected")]
    [InlineData("0009", "Rejected")]
    [InlineData("9999", "Unknown")]
    [InlineData("7777", "Unknown")]
    public void Exact_matching_response_codes_control_full_void_outcome(string code, string outcome)
    {
        var operation = Sample(); Assert.Equal(outcome, VoidProtocol.Parse(BankOperation.MerchantVoid, Reply(operation, code), operation).Outcome);
        Assert.Equal(code == "0000" ? "Confirmed" : "Unknown", VoidProtocol.Parse(BankOperation.EnquiryVoid, Reply(operation, code), operation).Outcome);
    }
    [Fact]
    public void Echo_mismatch_duplicate_keys_or_transport_failure_never_confirm_a_void()
    {
        var operation = Sample();
        Assert.Equal("Unknown", VoidProtocol.Parse(BankOperation.MerchantVoid, Reply(withDifferentTransaction(), "0000"), operation).Outcome);
        var good = Reply(operation, "0000"); var duplicate = Encoding.UTF8.GetString(good.Body).Replace("{", "{\"ResCode\":\"0000\",", StringComparison.Ordinal);
        Assert.Equal("Unknown", VoidProtocol.Parse(BankOperation.MerchantVoid, good with { Body = Encoding.UTF8.GetBytes(duplicate) }, operation).Outcome);
        Assert.Equal("Unknown", VoidProtocol.Parse(BankOperation.MerchantVoid, good with { TransportError = "Timeout" }, operation).Outcome);
        VoidOperation withDifferentTransaction() { var other = Sample(); other.TransactionId = "TST20261018100001"; return other; }
    }
    internal static VoidOperation Sample() => new() { TransactionId = "TST20261018100000", InstructionId = "ORIGINAL", QrId = "ZTSTE261018235900001", BillerNo = "111111111111111" };
    internal static BankReply Reply(VoidOperation operation, string code) => new(200, JsonSerializer.SerializeToUtf8Bytes(new
    { operation.TransactionId, operation.InstructionId, QRId = operation.QrId, operation.BillerNo, ResCode = code, ResDesc = "Synthetic bank response", TransDate = "20261018100005" }));
}

public sealed class VoidPdfTests
{
    [Fact]
    public async Task Actual_chromium_void_document_renders_Thai_and_legacy_receipt_hash_shape_is_preserved()
    {
        var original = ReceiptTests.Sample(); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacyJson = JsonSerializer.Serialize(original, options); Assert.DoesNotContain("\"void\"", legacyJson);
        var oldDocument = new CommerceDocument { Kind = "Receipt", Number = original.Number, SnapshotJson = legacyJson, SnapshotHash = SecretTokens.Hash(legacyJson) };
        Assert.Equal(original.Number, ReceiptTemplate.Read(oldDocument).Number); Assert.Null(ReceiptTemplate.Read(oldDocument).Void);
        var info = new VoidDocumentSnapshot(original.Number, "ZTSTE261018235900001", "ORDER123", "SHOP1", original.TotalMinor,
            "TST20261018100100", "เจ้าหน้าที่การเงิน ทดสอบ", "ผู้ซื้อขอยกเลิกรายการและคืนเงินเต็มจำนวน", "MerchantVoid:Confirmed", "SYNTHETIC-EVIDENCE", original.ConfirmedAt.AddMinutes(2));
        var snapshot = original with { Number = "LV20261018000001", IssuedAt = info.ObservedAt.AddMinutes(1), Void = info };
        var html = ReceiptTemplate.Html(snapshot with { Void = info with { Reason = "<script>alert(1)</script>" } }); Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html);
        var pdf = await new ChromiumReceiptRenderer(new()).RenderAsync(snapshot, default); Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5)); Assert.True(pdf.Length > 10000);
        var root = Directory.GetCurrentDirectory(); while (!Directory.Exists(Path.Combine(root, "Documentation"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace missing.");
        var path = Path.Combine(root, ".artifacts", "pdf-qa", "void-thai.pdf"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await System.IO.File.WriteAllBytesAsync(path, pdf);
    }
}

public sealed class VoidIntegrationTests
{
    private sealed class LoseMockVoidReply(IBankGateway inner) : IBankGateway
    {
        public void EnsureAvailable() => inner.EnsureAvailable();
        public async Task<BankReply> PostAsync(BankOperation operation, object body, Guid requestUid, CancellationToken cancellationToken)
        {
            var reply = await inner.PostAsync(operation, body, requestUid, cancellationToken);
            return operation == BankOperation.MerchantVoid ? new(null, [], "SyntheticLostVoidReply") : reply;
        }
    }

    [PgFact]
    public async Task Local_simulator_commits_remote_void_before_a_lost_reply_and_enquiry_recovers_without_real_bank_access()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var db = database.Open(); var buyer = await CartTests.ShopperAsync(db, "void-local-buyer");
        var variant = (await CatalogTests.PublishedAsync(db, "void-local-product")).Variant; var quote = await OrderTests.QuoteAsync(db, buyer, (variant.Id, 1));
        var order = await OrderTests.Service(db).CreateAsync(buyer.ShopperId, new(quote.Id), "void-local-order", default);
        var commerce = new CommerceOptions(); var bankOptions = new TtbOptions(); await new QrService(db, new(db, TimeProvider.System), commerce, bankOptions, new(commerce, bankOptions), TimeProvider.System).GetOrCreateAsync(buyer.ShopperId, order.Id, default);
        var payments = new PaymentProcessor(db, commerce, bankOptions, TimeProvider.System); await new MockBankService(db, commerce, payments, TimeProvider.System).PayAsync(buyer.ShopperId, order.Id, new("void-local-credit"), default);
        var clock = new MutableClock(DateTimeOffset.UtcNow); var policy = Policy(); policy.LocalSimulationEnabled = true; policy.WindowPolicy = VoidWindowPolicy.BankingDay2256; policy.CutoffSafetySeconds = 0;
        await db.Attempts.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, clock.GetUtcNow().AddSeconds(-1)));
        await db.BankTransactions.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, clock.GetUtcNow()).SetProperty(x => x.PaymentOccurredAt, clock.GetUtcNow()).SetProperty(x => x.PaymentTimeSource, "ReportIsoPayment"));
        var gateway = new LoseMockVoidReply(new MockBankGateway(db, commerce, clock)); var service = new VoidService(db, commerce, bankOptions, policy, gateway, new(db, clock), clock);
        var intent = await service.RequestAsync(order.Id, new(Guid.NewGuid().ToString("D"), "Local-only refund simulation"), Finance, default); await service.SendAsync(intent.Id, default);
        Assert.Equal("Unknown", (await service.GetAsync(intent.Id, Finance, default)).State); Assert.Equal(1, await db.MockVoids.CountAsync()); Assert.False((await db.BankTransactions.SingleAsync()).Voided);
        Assert.Equal("Confirmed", (await service.EnquireAsync(intent.Id, Finance, default)).State); Assert.True((await db.BankTransactions.AsNoTracking().SingleAsync()).Voided);
        var raw = Encoding.UTF8.GetBytes((await db.MockCredits.SingleAsync()).NoticeJson); var evidence = BankWire.ParseNotice(raw) with { ResultCode = "0000" };
        var after = await new PaymentProcessor(db, commerce, bankOptions, clock).ApplyAsync(evidence, raw, new(EvidenceSource.MockVerify, "after-void"), default);
        Assert.Equal("AlreadyVoided", after.Outcome); Assert.Equal(PaymentState.Voided, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState); Assert.Equal(1, await db.Allocations.CountAsync());
        Assert.Throws<CommerceException>(() => new MockBankGateway(db, PaymentFixture.Commerce, clock).EnsureAvailable());
    }

    private sealed class VoidApiFactory(string connection, IBankGateway gateway, VoidOptions options, TimeProvider clock) : CommerceApiFactory(connection, enableLocal: false, environment: CommerceEnvironment.Uat)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBankGateway>(); services.AddSingleton(gateway); services.RemoveAll<TtbOptions>(); services.AddSingleton(PaymentFixture.Bank);
                services.RemoveAll<VoidOptions>(); services.AddSingleton(options); services.RemoveAll<TimeProvider>(); services.AddSingleton(clock);
            });
        }
    }
    private sealed class FakeGateway(PgDatabase database) : IBankGateway
    {
        public int Sends; public int Enquiries; public bool LoseSend; public string SendCode = "0000"; public string EnquiryCode = "0000";
        public bool BlockUntilCancelled; public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void EnsureAvailable() { }
        public async Task<BankReply> PostAsync(BankOperation operation, object body, Guid requestUid, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.SerializeToElement(body); var id = json.GetProperty("TransactionId").GetString();
            await using var db = database.Open(); var persisted = await db.Voids.AsNoTracking().SingleAsync(x => x.TransactionId == id, cancellationToken);
            Assert.NotNull(persisted.SendFencedAt);
            if (persisted.AffectsOrderPayment) Assert.Equal(PaymentState.VoidPending, (await db.Orders.SingleAsync(x => x.Id == persisted.OrderId, cancellationToken)).PaymentState);
            Called.TrySetResult(); if (BlockUntilCancelled) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (operation == BankOperation.MerchantVoid) { Interlocked.Increment(ref Sends); if (LoseSend) return new(null, [], "Timeout"); return VoidProtocolTests.Reply(persisted, SendCode); }
            Interlocked.Increment(ref Enquiries); return VoidProtocolTests.Reply(persisted, EnquiryCode);
        }
    }
    private static VoidOptions Policy() => new() { Enabled = true, ProfileApproved = true, WindowPolicy = VoidWindowPolicy.SameCalendarDayBefore2256 };
    private static ShopperActor Finance => new(Guid.NewGuid(), "mangosgo", "finance", "Finance Test", ["finance"]);
    private static VoidService Service(CommerceDb db, FakeGateway gateway, MutableClock clock, VoidOptions? policy = null) =>
        new(db, PaymentFixture.Commerce, PaymentFixture.Bank, policy ?? Policy(), gateway, new(db, clock), clock);
    private static async Task<(PaymentFixture Fixture, MutableClock Clock)> PaidAsync(PgDatabase database)
    {
        var (fixture, _) = await ReportFixtures.PaidAsync(database); var at = DateTimeOffset.Parse("2026-10-18T10:00:00+07:00", CultureInfo.InvariantCulture);
        await using var db = database.Open(); await db.BankTransactions.ExecuteUpdateAsync(s => s.SetProperty(x => x.PaymentOccurredAt, at.ToUniversalTime()).SetProperty(x => x.PaymentTimeSource, "ReportIsoPayment").SetProperty(x => x.CreatedAt, at.AddSeconds(5).ToUniversalTime()));
        await db.Attempts.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, at.AddMinutes(-1).ToUniversalTime()));
        return (fixture, new(at.AddMinutes(1).ToUniversalTime()));
    }

    [PgFact]
    public async Task Extra_credit_void_preserves_primary_allocation_entitlements_and_original_receipt()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage();
        var (fixture, clock) = await PaidAsync(database); var gateway = new FakeGateway(database); var actor = Finance;
        await using var db = database.Open(); var primary = await db.BankTransactions.SingleAsync();
        var line = await db.OrderLines.SingleAsync();
        db.Entitlements.Add(new Entitlement { OrderId = fixture.OrderId, OrderLineId = line.Id, ShopperId = fixture.ShopperId, UnitIndex = 0, State = EntitlementState.Active });
        var extra = new BankTransaction { Environment = primary.Environment, MerchantId = primary.MerchantId, BillerNo = primary.BillerNo, InstructionId = "EXTRA-SECOND-CREDIT", QrId = primary.QrId,
            Ref1 = primary.Ref1, Ref2 = primary.Ref2, AmountMinor = primary.AmountMinor, CreatedAt = primary.CreatedAt, PaymentOccurredAt = primary.PaymentOccurredAt, PaymentTimeSource = primary.PaymentTimeSource };
        db.BankTransactions.Add(extra); await db.SaveChangesAsync();
        var receipts = new ReceiptService(db, new(db, clock), PaymentFixture.Commerce, new(), clock, storage.Files);
        var receipt = await receipts.EnsureAsync(fixture.OrderId, default); var hash = (await db.Documents.SingleAsync()).SnapshotHash;
        var service = Service(db, gateway, clock); var input = new VoidInput(Guid.NewGuid().ToString("D"), "Return duplicate credit only");
        var intent = await service.RequestBankAsync(extra.Id, input, actor, default);
        Assert.Equal(intent.Id, (await service.RequestBankAsync(extra.Id, input, actor, default)).Id);
        Assert.False((await db.Voids.SingleAsync()).AffectsOrderPayment); await service.SendAsync(intent.Id, default);
        Assert.Equal(PaymentState.Paid, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState);
        Assert.False((await db.BankTransactions.AsNoTracking().SingleAsync(x => x.Id == primary.Id)).Voided);
        Assert.True((await db.BankTransactions.AsNoTracking().SingleAsync(x => x.Id == extra.Id)).Voided);
        Assert.Equal(EntitlementState.Active, (await db.Entitlements.AsNoTracking().SingleAsync()).State);
        Assert.Equal(primary.Id, (await db.Allocations.SingleAsync()).BankTransactionId);
        var docs = new VoidDocumentHandler(db, receipts, new(db, clock), PaymentFixture.Commerce, clock);
        await docs.EnsureAsync(fixture.OrderId, extra.Id, "MerchantVoid", "test-proof", clock.GetUtcNow(), default);
        Assert.Equal(hash, (await db.Documents.SingleAsync(x => x.Id == receipt.Id)).SnapshotHash);
        var extraSnapshot = ReceiptTemplate.Read(await db.Documents.SingleAsync(x => x.Kind == "Void"));
        Assert.Equal("ไม่มีใบเสร็จสำหรับยอดรับเงินนี้", extraSnapshot.Void!.OriginalReceiptNumber);
        Assert.Equal(extra.InstructionId, extraSnapshot.PaymentReference); Assert.Empty(extraSnapshot.Lines);
        // A subsequent primary refund has its own document; the old per-order/kind uniqueness must not block it.
        clock.Advance(TimeSpan.FromSeconds(1)); var primaryIntent = await service.RequestAsync(fixture.OrderId, new(Guid.NewGuid().ToString("D"), "Return primary purchase"), actor, default);
        await service.SendAsync(primaryIntent.Id, default); await docs.EnsureAsync(fixture.OrderId, primary.Id, "MerchantVoid", "primary-proof", clock.GetUtcNow(), default);
        Assert.Equal(3, await db.Documents.CountAsync()); Assert.Equal(2, await db.Documents.CountAsync(x => x.Kind == "Void"));
    }

    [PgFact]
    public async Task Unmatched_credit_requires_original_time_proof_and_issues_finance_only_void_document_without_fabricating_order()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, clock) = await PaidAsync(database);
        var gateway = new FakeGateway(database); await using var db = database.Open(); var primary = await db.BankTransactions.SingleAsync();
        var credit = new BankTransaction { Environment = primary.Environment, MerchantId = primary.MerchantId, BillerNo = primary.BillerNo, InstructionId = "UNMATCHED-CREDIT", QrId = "UNMATCHED-QR",
            Ref1 = "UNKNOWN", Ref2 = "UNKNOWN", AmountMinor = 1000, CreatedAt = clock.GetUtcNow().AddSeconds(-10) };
        db.BankTransactions.Add(credit); await db.SaveChangesAsync(); var service = Service(db, gateway, clock); var input = new VoidInput(Guid.NewGuid().ToString("D"), "Return unmatched credit with proof");
        Assert.Equal("void_original_time_unproven", (await Assert.ThrowsAsync<CommerceException>(() => service.RequestBankAsync(credit.Id, input, Finance, default))).Code);
        await db.BankTransactions.Where(x => x.Id == credit.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PaymentOccurredAt, clock.GetUtcNow().AddSeconds(-20)).SetProperty(x => x.PaymentTimeSource, "ReportIsoPayment"));
        var intent = await service.RequestBankAsync(credit.Id, input, Finance, default); Assert.Null(intent.OrderId); await service.SendAsync(intent.Id, default);
        Assert.Equal("Confirmed", (await service.GetAsync(intent.Id, Finance, default)).State);
        var receipts = new ReceiptService(db, new(db, clock), PaymentFixture.Commerce, new(), clock, storage.Files);
        var docs = new VoidDocumentHandler(db, receipts, new(db, clock), PaymentFixture.Commerce, clock);
        var document = await docs.EnsureAsync(null, credit.Id, "MerchantVoid", "unmatched-proof", clock.GetUtcNow(), default);
        Assert.Null((await db.Documents.SingleAsync()).OrderId); Assert.Equal(0, await db.Documents.CountAsync(x => x.Kind == "Receipt"));
        Assert.Equal(1, await db.Orders.CountAsync()); Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Equal(PaymentState.Paid, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState);
        await new DocumentRenderHandler(db, storage.Files, new ReceiptTests.FakeRenderer(), clock).HandleAsync(document.Id, default);
        Assert.Equal("not_found", (await Assert.ThrowsAsync<CommerceException>(() => receipts.DownloadAsync(fixture.ShopperId, document.Id, "buyer", "test", default))).Code);
        var download = await receipts.DownloadAsync(null, document.Id, "finance", "test", default); await download.Stream.DisposeAsync();
        Assert.Empty(await receipts.OwnAsync(fixture.ShopperId, fixture.OrderId, default));
    }

    [PgFact]
    public async Task HTTP_void_requires_finance_and_CSRF_and_shopper_activity_reports_pending_then_confirmed()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, _) = await PaidAsync(database); var gateway = new FakeGateway(database);
        var clock = new MutableClock(DateTimeOffset.UtcNow); var policy = Policy(); policy.WindowPolicy = VoidWindowPolicy.BankingDay2256; policy.CutoffSafetySeconds = 0;
        await using (var db = database.Open())
        {
            await db.Shoppers.Where(x => x.Id == fixture.ShopperId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Issuer, "mangosgo").SetProperty(x => x.Subject, "buyer"));
            await db.BankTransactions.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, clock.GetUtcNow()).SetProperty(x => x.PaymentOccurredAt, clock.GetUtcNow()));
            await db.Attempts.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, clock.GetUtcNow().AddSeconds(-1)));
        }
        await using var factory = new VoidApiFactory(database.ConnectionString, gateway, policy, clock);
        using var buyer = factory.ShopperClient("buyer"); using var admin = factory.ShopperClient("admin"); await CartTests.SignInAsync(buyer); var session = await CartTests.SignInAsync(admin);
        var path = $"/ttb-commerce/v1/admin/orders/{fixture.OrderId}/void"; var input = new VoidInput(Guid.NewGuid().ToString("D"), "Customer requested full cancellation");
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsJsonAsync(path, input)).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-Token"); Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(path, input)).StatusCode);
        admin.DefaultRequestHeaders.Add("X-CSRF-Token", session.GetProperty("csrfToken").GetString()); var response = await admin.PostAsJsonAsync(path, input); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var intent = await response.Content.ReadFromJsonAsync<VoidView>(); Assert.NotNull(intent); Assert.Equal(0, gateway.Sends);
        var activity = await buyer.GetFromJsonAsync<OrderActivity>($"/ttb-commerce/v1/orders/{fixture.OrderId}/activity"); Assert.Equal("VoidPending", activity!.Order.PaymentState); Assert.False(activity.Payment!.Voided);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync($"/ttb-commerce/v1/admin/voids/{intent.Id}")).StatusCode);
        await using (var db = database.Open()) await Service(db, gateway, clock, policy).SendAsync(intent.Id, default);
        activity = await buyer.GetFromJsonAsync<OrderActivity>($"/ttb-commerce/v1/orders/{fixture.OrderId}/activity"); Assert.Equal("Voided", activity!.Order.PaymentState); Assert.True(activity.Payment!.Voided);
        var result = await admin.GetAsync($"/ttb-commerce/v1/admin/voids/{intent.Id}"); Assert.True(result.Headers.CacheControl?.NoStore); Assert.Equal("Confirmed", (await result.Content.ReadFromJsonAsync<VoidView>())!.State);
    }

    [PgFact]
    public async Task Cancellation_after_the_committed_fence_preserves_unknown_evidence_and_does_not_retry_the_mutation()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, clock) = await PaidAsync(database); var gateway = new FakeGateway(database) { BlockUntilCancelled = true }; var actor = Finance;
        await using var db = database.Open(); var service = Service(db, gateway, clock); var intent = await service.RequestAsync(fixture.OrderId, new(Guid.NewGuid().ToString("D"), "Cancel"), actor, default);
        using var cancellation = new CancellationTokenSource(); var sending = service.SendAsync(intent.Id, cancellation.Token);
        await gateway.Called.Task.WaitAsync(TimeSpan.FromSeconds(20)); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        await service.SendAsync(intent.Id, default); var row = await db.Voids.AsNoTracking().SingleAsync(); Assert.Equal("Unknown", row.State); Assert.NotNull(row.SendFencedAt);
        Assert.Equal("Unknown", (await db.VoidRequests.AsNoTracking().SingleAsync()).Outcome); Assert.Equal(PaymentState.VoidPending, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState);
    }

    [PgFact]
    public async Task Definitive_rejection_requires_a_new_explicit_operation_and_never_reuses_a_second_based_id()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, clock) = await PaidAsync(database); var gateway = new FakeGateway(database) { SendCode = "0008" }; var actor = Finance;
        await using var db = database.Open(); var service = Service(db, gateway, clock); var input = new VoidInput(Guid.NewGuid().ToString("D"), "Cancel");
        var first = await service.RequestAsync(fixture.OrderId, input, actor, default); await service.SendAsync(first.Id, default);
        Assert.Equal("Rejected", (await service.GetAsync(first.Id, actor, default)).State); Assert.Equal(PaymentState.Paid, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState);
        Assert.Equal(OrderState.Review, (await db.Orders.AsNoTracking().SingleAsync()).State); await service.SendAsync(first.Id, default); Assert.Equal(1, gateway.Sends);
        Assert.Equal(first.Id, (await service.RequestAsync(fixture.OrderId, input, actor, default)).Id);
        Assert.Equal("operation_conflict", (await Assert.ThrowsAsync<CommerceException>(() => service.RequestAsync(fixture.OrderId, input with { Reason = "different" }, actor, default))).Code);
        var secondInput = new VoidInput(Guid.NewGuid().ToString("D"), "Funds restored; explicit retry");
        Assert.Equal("numbering_exhausted", (await Assert.ThrowsAsync<CommerceException>(() => service.RequestAsync(fixture.OrderId, secondInput, actor, default))).Code);
        clock.Advance(TimeSpan.FromSeconds(1)); gateway.SendCode = "0000"; var second = await service.RequestAsync(fixture.OrderId, secondInput, actor, default);
        Assert.NotEqual(first.TransactionId, second.TransactionId); await service.SendAsync(second.Id, default); Assert.Equal(2, gateway.Sends);
        Assert.Equal(2, await db.Voids.CountAsync()); Assert.Equal(1, await db.Jobs.CountAsync(x => x.Kind == "void-document.ensure"));
    }

    [PgFact]
    public async Task A_queued_void_that_misses_cutoff_is_rejected_without_network_and_used_goods_require_explicit_policy()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, clock) = await PaidAsync(database); var gateway = new FakeGateway(database); var actor = Finance;
        await using var db = database.Open(); var policy = Policy(); var service = Service(db, gateway, clock, policy);
        var line = await db.OrderLines.SingleAsync(); db.Fulfillments.Add(new FulfillmentItem { OrderId = fixture.OrderId, OrderLineId = line.Id, Kind = ProductKind.Physical, Quantity = 1, DeliveryMethod = "Pickup", State = FulfillmentState.Delivered }); await db.SaveChangesAsync();
        var input = new VoidInput(Guid.NewGuid().ToString("D"), "Returned item", true);
        Assert.Equal("void_delivery_review_required", (await Assert.ThrowsAsync<CommerceException>(() => service.RequestAsync(fixture.OrderId, input, actor, default))).Code);
        policy.FulfilledRefundPolicyApproved = true;
        Assert.Equal("void_delivery_review_required", (await Assert.ThrowsAsync<CommerceException>(() => service.RequestAsync(fixture.OrderId, input with { DeliveryReviewed = false }, actor, default))).Code);
        var intent = await service.RequestAsync(fixture.OrderId, input, actor, default); clock.Advance(TimeSpan.FromHours(13)); await service.SendAsync(intent.Id, default);
        Assert.Equal(0, gateway.Sends); Assert.Equal("Rejected", (await service.GetAsync(intent.Id, actor, default)).State); Assert.Equal(PaymentState.Paid, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState);
        Assert.Equal(FulfillmentState.Delivered, (await db.Fulfillments.AsNoTracking().SingleAsync()).State);
    }

    [PgFact]
    public async Task Void_document_is_confirmed_only_immutable_concurrent_and_preserves_original_receipt_hash()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, clock) = await PaidAsync(database); var gateway = new FakeGateway(database); var actor = Finance;
        ReceiptService Receipts(CommerceDb db) => new(db, new(db, clock), PaymentFixture.Commerce, new(), clock, storage.Files);
        VoidDocumentHandler Documents(CommerceDb db) => new(db, Receipts(db), new(db, clock), PaymentFixture.Commerce, clock);
        Guid bankId; string originalHash; Guid originalId; VoidView intent;
        await using (var db = database.Open())
        {
            bankId = await db.BankTransactions.Select(x => x.Id).SingleAsync(); var receipt = await Receipts(db).EnsureAsync(fixture.OrderId, default); originalId = receipt.Id;
            var original = await db.Documents.SingleAsync(); originalHash = original.SnapshotHash; Assert.DoesNotContain("\"void\"", original.SnapshotJson);
            Assert.Null(ReceiptTemplate.Read(original).Void);
            await Assert.ThrowsAsync<CommerceException>(() => Documents(db).EnsureAsync(fixture.OrderId, bankId, "Unconfirmed", "test", clock.GetUtcNow(), default));
            intent = await Service(db, gateway, clock).RequestAsync(fixture.OrderId, new(Guid.NewGuid().ToString("D"), "คืนสินค้าเต็มจำนวน"), actor, default); await Service(db, gateway, clock).SendAsync(intent.Id, default);
        }
        var documents = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => { await using var db = database.Open(); return await Documents(db).EnsureAsync(fixture.OrderId, bankId, "MerchantVoid", "synthetic-proof", clock.GetUtcNow(), default); }));
        Assert.Single(documents.Select(x => x.Id).Distinct());
        await using (var db = database.Open())
        {
            Assert.Equal(2, await db.Documents.CountAsync()); var original = await db.Documents.SingleAsync(x => x.Id == originalId); Assert.Equal(originalHash, original.SnapshotHash);
            var document = await db.Documents.SingleAsync(x => x.Kind == "Void"); var snapshot = ReceiptTemplate.Read(document);
            Assert.Equal(intent.TransactionId, snapshot.Void!.VoidTransactionId); Assert.Equal(fixture.Notice.InstructionId, snapshot.PaymentReference); Assert.Equal(2, await db.Jobs.CountAsync(x => x.Kind == "document.render"));
            var html = ReceiptTemplate.Html(snapshot); Assert.Contains("หลักฐานยกเลิกรายการ", html); Assert.Contains("ไม่ใช่ใบกำกับภาษี", html); Assert.Contains("ไม่ใช่การระบุเวลาที่เงินถึงบัญชี", html);
        }
    }

    [PgFact]
    public async Task Concurrent_intents_and_job_retries_send_one_full_void_after_the_durable_fence()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, clock) = await PaidAsync(database); var gateway = new FakeGateway(database); var actor = Finance;
        var request = new VoidInput(Guid.NewGuid().ToString("D"), "Customer cancellation");
        var views = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); return await Service(db, gateway, clock).RequestAsync(fixture.OrderId, request, actor, default); }));
        Assert.Single(views.Select(x => x.Id).Distinct()); Assert.Equal(17, views[0].TransactionId.Length);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); await Service(db, gateway, clock).SendAsync(views[0].Id, default); }));
        Assert.Equal(1, gateway.Sends);
        await using var check = database.Open(); Assert.Equal("Confirmed", (await check.Voids.SingleAsync()).State); Assert.True((await check.BankTransactions.SingleAsync()).Voided);
        Assert.Equal(PaymentState.Voided, (await check.Orders.SingleAsync()).PaymentState); Assert.Equal(1, await check.Allocations.CountAsync());
        Assert.Equal(4, (await check.Variants.SingleAsync()).OnHand); Assert.Equal(1, await check.Jobs.CountAsync(x => x.Kind == "void-document.ensure"));
        Assert.Equal(1, await check.VoidRequests.CountAsync()); Assert.NotEmpty((await check.VoidRequests.SingleAsync()).RawResponse);
    }

    [PgFact]
    public async Task Lost_mutation_response_never_resends_and_original_transaction_enquiry_confirms()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, clock) = await PaidAsync(database); var gateway = new FakeGateway(database) { LoseSend = true }; var actor = Finance;
        await using var db = database.Open(); var service = Service(db, gateway, clock);
        var intent = await service.RequestAsync(fixture.OrderId, new(Guid.NewGuid().ToString("D"), "Cancel"), actor, default); await service.SendAsync(intent.Id, default);
        Assert.Equal("Unknown", (await service.GetAsync(intent.Id, actor, default)).State); await service.SendAsync(intent.Id, default); Assert.Equal(1, gateway.Sends);
        Assert.Equal(PaymentState.VoidPending, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState);
        var result = await service.EnquireAsync(intent.Id, actor, default); Assert.Equal("Confirmed", result.State); Assert.Equal(intent.TransactionId, result.TransactionId);
        Assert.Equal(1, gateway.Sends); Assert.Equal(1, gateway.Enquiries); Assert.Equal(2, await db.VoidRequests.CountAsync());
        Assert.Equal("Resolved", (await db.Cases.SingleAsync(x => x.Kind == "VoidOperationReview")).State);
    }

    [PgFact]
    public async Task Crash_after_fence_holds_delivery_and_never_repeats_mutation_even_if_it_never_reached_bank()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, clock) = await PaidAsync(database); var gateway = new FakeGateway(database) { EnquiryCode = "0003" }; var actor = Finance;
        await using var db = database.Open(); var service = Service(db, gateway, clock);
        var intent = await service.RequestAsync(fixture.OrderId, new(Guid.NewGuid().ToString("D"), "Cancel"), actor, default);
        await db.Voids.Where(x => x.Id == intent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Unknown").SetProperty(x => x.SendFencedAt, clock.GetUtcNow()));
        await service.SendAsync(intent.Id, default); Assert.Equal(0, gateway.Sends);
        Assert.Equal("Unknown", (await service.EnquireAsync(intent.Id, actor, default)).State);
        Assert.Equal(PaymentState.VoidPending, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState); Assert.False((await db.BankTransactions.AsNoTracking().SingleAsync()).Voided);
        Assert.Equal("void_enquiry_cooldown", (await Assert.ThrowsAsync<CommerceException>(() => service.EnquireAsync(intent.Id, actor, default))).Code);
        for (var i = 0; i < 2; i++) { clock.Advance(TimeSpan.FromSeconds(31)); await service.EnquireAsync(intent.Id, actor, default); }
        clock.Advance(TimeSpan.FromSeconds(31)); Assert.Equal("void_enquiry_budget_exhausted", (await Assert.ThrowsAsync<CommerceException>(() => service.EnquireAsync(intent.Id, actor, default))).Code);
        Assert.Equal(3, gateway.Enquiries); Assert.Equal(0, gateway.Sends);
    }
}
