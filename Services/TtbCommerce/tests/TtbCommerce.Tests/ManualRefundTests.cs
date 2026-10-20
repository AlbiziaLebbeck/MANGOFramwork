using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class ManualRefundTests
{
    private static ShopperActor Maker => new(Guid.NewGuid(), "mangosgo", "finance-maker", "Finance maker", ["finance"]);
    private static ShopperActor Checker => new(Guid.NewGuid(), "mangosgo", "finance-checker", "Finance checker", ["finance"]);
    private static ManualRefundOptions Policy() => new() { ExternalRefundAttestationApproved = true, GoodsDispositionPolicyApproved = true };
    private static ManualRefundService Service(CommerceDb db, TestPrivateStorage storage, MutableClock clock, ManualRefundOptions? policy = null) => new(db, PaymentFixture.Commerce, PaymentFixture.Bank, policy ?? Policy(), clock, storage.Files);
    private static FinanceEvidenceService Evidence(CommerceDb db, TestPrivateStorage storage, MutableClock clock) => new(db, PaymentFixture.Commerce, PaymentFixture.Bank, Policy(), storage.Files, clock);
    private static string Key() => Guid.NewGuid().ToString("D");
    private static string Minor(long amount) => amount.ToString(CultureInfo.InvariantCulture);
    private static async Task<(PaymentFixture Fixture, MutableClock Clock, Guid BankId, long Amount)> PaidAsync(PgDatabase database, DateTimeOffset? now = null)
    {
        var fixture = await PaymentFixture.CreateAsync(database); await using var db = database.Open();
        await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default);
        var clock = new MutableClock(now ?? DateTimeOffset.Parse("2026-10-19T10:01:00+07:00", CultureInfo.InvariantCulture));
        await db.BankTransactions.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, clock.GetUtcNow().AddSeconds(-55)).SetProperty(x => x.PaymentOccurredAt, clock.GetUtcNow().AddSeconds(-59)).SetProperty(x => x.PaymentTimeSource, "ReportIsoPayment"));
        await db.Attempts.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, clock.GetUtcNow().AddMinutes(-1)));
        await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(x => x.ConfirmedAt, clock.GetUtcNow().AddSeconds(-55)));
        var bank = await db.BankTransactions.AsNoTracking().SingleAsync(); return (fixture, clock, bank.Id, bank.AmountMinor);
    }
    private static async Task<FinanceEvidenceView> ProofAsync(CommerceDb db, TestPrivateStorage storage, MutableClock clock, Guid caseId, ShopperActor actor, string text = "Synthetic external transfer proof")
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text)); return await Evidence(db, storage, clock).UploadAsync(caseId, input, "หลักฐานทดสอบ.txt", actor, default);
    }
    private static async Task<ManualRefundView> SubmitAsync(CommerceDb db, TestPrivateStorage storage, MutableClock clock, ManualRefundView row, ShopperActor actor)
    {
        var file = await ProofAsync(db, storage, clock, row.CaseId, actor); var service = Service(db, storage, clock); row = await service.GetAsync(row.Id, actor, default);
        return await service.ActAsync(row.Id, new(Key(), row.Version, "submit-evidence", "External transfer declared; requires independent review", "SYNTHETIC-EXTERNAL-REFERENCE", clock.GetUtcNow().AddSeconds(-1), [file.Id]), actor, default);
    }
    private sealed class NoNetworkGateway : IBankGateway
    {
        public int Calls;
        public void EnsureAvailable() { }
        public Task<BankReply> PostAsync(BankOperation operation, object body, Guid requestUid, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); throw new InvalidOperationException("No financial network access is permitted in manual-case tests."); }
    }
    private sealed class ManualApiFactory(string connection, MutableClock clock) : CommerceApiFactory(connection, enableLocal: false, environment: CommerceEnvironment.Uat)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Commerce:FinanceSubjects:0"] = "mangosgo:finance-checker" }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TtbOptions>(); services.AddSingleton(PaymentFixture.Bank);
                services.RemoveAll<ManualRefundOptions>(); services.AddSingleton(Policy());
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
                services.RemoveAll<IBankGateway>(); services.AddSingleton<IBankGateway>(new NoNetworkGateway());
            });
        }
    }

    [PgFact]
    public async Task HTTP_manual_evidence_requires_finance_CSRF_and_stores_declared_offsets_as_UTC_without_exposing_files_to_shoppers()
    {
        // CookieContainer uses the real wall clock. Keep this HTTP fixture's six-hour session in the future;
        // service-only banking-window fixtures retain their fixed timestamps.
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, clock, bankId, amount) = await PaidAsync(database, DateTimeOffset.UtcNow);
        await using var factory = new ManualApiFactory(database.ConnectionString, clock) { PrivateRoot = storage.Root };
        using var admin = factory.ShopperClient("admin"); using var buyer = factory.ShopperClient("buyer"); using var checker = factory.ShopperClient("finance-checker");
        await CartTests.SignInAsync(admin); await CartTests.SignInAsync(buyer); await CartTests.SignInAsync(checker);
        Assert.True((await buyer.GetFromJsonAsync<JsonElement>("/ttb-commerce/v1/session")).GetProperty("authenticated").GetBoolean());
        var createPath = $"/ttb-commerce/v1/admin/bank-transactions/{bankId}/manual-refunds";
        buyer.DefaultRequestHeaders.Add("X-Role", "admin"); Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsJsonAsync(createPath, new ManualRefundInput(Key(), Minor(amount), "Forbidden buyer"))).StatusCode);
        var created = await admin.PostAsJsonAsync(createPath, new ManualRefundInput(Key(), Minor(amount), "HTTP external refund case")); Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var row = (await created.Content.ReadFromJsonAsync<ManualRefundView>())!; var proof = new ByteArrayContent(Encoding.UTF8.GetBytes("Synthetic HTTP finance evidence"));
        proof.Headers.ContentType = new("application/octet-stream"); admin.DefaultRequestHeaders.Add("X-File-Name", Uri.EscapeDataString("หลักฐาน.txt"));
        var uploaded = await admin.PostAsync($"/ttb-commerce/v1/admin/reconciliation/{row.CaseId}/evidence", proof); Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode); var file = (await uploaded.Content.ReadFromJsonAsync<FinanceEvidenceView>())!;
        var detail = await admin.GetFromJsonAsync<FinanceCaseDetail>($"/ttb-commerce/v1/admin/reconciliation/{row.CaseId}"); Assert.Equal(row.Id, detail!.ManualRefund!.Id); Assert.Single(detail.ManualRefund.Files); row = detail.ManualRefund;
        var actionPath = $"/ttb-commerce/v1/admin/manual-refunds/{row.Id}/actions";
        var submit = new ManualRefundActionInput(Key(), row.Version, "submit-evidence", "Declared timestamp carries Bangkok offset", "SYNTHETIC-HTTP-TRANSFER", clock.GetUtcNow().AddSeconds(-1).ToOffset(TimeSpan.FromHours(7)), [file.Id]);
        var submitted = await admin.PostAsJsonAsync(actionPath, submit); Assert.Equal(HttpStatusCode.OK, submitted.StatusCode); row = (await submitted.Content.ReadFromJsonAsync<ManualRefundView>())!;
        await using (var db = database.Open()) Assert.Equal(TimeSpan.Zero, (await db.ManualRefunds.SingleAsync()).ExternalPaidAt!.Value.Offset);
        var downloadPath = $"/ttb-commerce/v1/admin/finance-evidence/{file.Id}/download";
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync(downloadPath)).StatusCode); var download = await checker.GetAsync(downloadPath); Assert.Equal(HttpStatusCode.OK, download.StatusCode); Assert.True(download.Headers.CacheControl?.NoStore); Assert.Equal("application/octet-stream", download.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Synthetic HTTP", await download.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/ttb-commerce/v1/documents/{file.Id}/download")).StatusCode);
        Assert.Empty(await admin.GetFromJsonAsync<DigitalAsset[]>("/ttb-commerce/v1/admin/digital-assets") ?? []);
        await using (var db = database.Open())
        {
            var buyerId = await db.Shoppers.Where(x => x.Issuer == "mangosgo" && x.Subject == "buyer").Select(x => x.Id).SingleAsync();
            await db.Orders.Where(x => x.Id == fixture.OrderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ShopperId, buyerId));
        }
        var beforeApproval = await buyer.GetFromJsonAsync<OrderActivity>($"/ttb-commerce/v1/orders/{fixture.OrderId}/activity"); Assert.Null(beforeApproval!.Refund);
        var approved = await checker.PostAsJsonAsync(actionPath, new ManualRefundActionInput(Key(), row.Version, "approve-attestation", "Second officer checked private transfer proof")); Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var activityResponse = await buyer.GetAsync($"/ttb-commerce/v1/orders/{fixture.OrderId}/activity"); Assert.True(activityResponse.Headers.CacheControl?.NoStore);
        var activityJson = await activityResponse.Content.ReadAsStringAsync(); var activity = JsonSerializer.Deserialize<OrderActivity>(activityJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(Minor(amount), activity.Refund!.AmountMinor); Assert.Equal("ApprovedManualAttestation", activity.Refund.Source); Assert.True(activity.Refund.GoodsReviewPending); Assert.Equal("Refunded", activity.Order.PaymentState); Assert.False(activity.Payment!.Voided);
        Assert.DoesNotContain(file.Id.ToString(), activityJson); Assert.DoesNotContain(file.Sha256, activityJson); Assert.DoesNotContain("SYNTHETIC-HTTP-TRANSFER", activityJson); Assert.DoesNotContain("หลักฐาน.txt", activityJson);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/ttb-commerce/v1/orders/{fixture.OrderId}/activity")).StatusCode);
        checker.DefaultRequestHeaders.Remove("X-CSRF-Token"); Assert.Equal(HttpStatusCode.Forbidden, (await checker.PostAsJsonAsync(actionPath, new ManualRefundActionInput(Key(), row.Version, "approve-attestation", "Missing CSRF"))).StatusCode);
    }

    [PgFact]
    public async Task Partial_cross_border_and_out_of_window_cases_reserve_exact_credit_without_a_bank_mutation_and_retry_once()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (_, clock, bankId, amount) = await PaidAsync(database); var maker = Maker;
        await using (var db = database.Open()) await db.BankTransactions.Where(x => x.Id == bankId).ExecuteUpdateAsync(s => s.SetProperty(x => x.CrossBorderFlag, "Y").SetProperty(x => x.PaymentOccurredAt, clock.GetUtcNow().AddDays(-2)));
        var input = new ManualRefundInput(Key(), Minor(amount / 2), "Partial external refund of a cross-border/old credit");
        var rows = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); return await Service(db, storage, clock).CreateAsync(bankId, input, maker, default); }));
        Assert.Single(rows.Select(x => x.Id).Distinct()); await using var check = database.Open(); var service = Service(check, storage, clock);
        await service.CreateAsync(bankId, new(Key(), Minor(amount - amount / 2), "Remaining partial amount"), maker, default);
        Assert.Equal("manual_refund_credit_exhausted", (await Assert.ThrowsAsync<CommerceException>(() => service.CreateAsync(bankId, new(Key(), "1", "Would exceed original credit"), maker, default))).Code);
        Assert.Equal("operation_conflict", (await Assert.ThrowsAsync<CommerceException>(() => service.CreateAsync(bankId, input with { Reason = "Changed input" }, maker, default))).Code);
        Assert.Equal(2, await check.ManualRefunds.CountAsync()); Assert.Equal(0, await check.Voids.CountAsync()); Assert.Equal(0, await check.VoidRequests.CountAsync());
        Assert.Equal(PaymentState.Paid, (await check.Orders.SingleAsync()).PaymentState); Assert.Equal(OrderState.Review, (await check.Orders.SingleAsync()).State); Assert.False((await check.BankTransactions.SingleAsync()).Voided);
    }

    [PgFact]
    public async Task A_manual_case_and_a_void_race_can_reserve_the_original_credit_only_once()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, clock, bankId, amount) = await PaidAsync(database); var maker = Maker; var gateway = new NoNetworkGateway();
        async Task<string> Manual() { await using var db = database.Open(); try { await Service(db, storage, clock).CreateAsync(bankId, new(Key(), Minor(amount), "Manual review"), maker, default); return "manual"; } catch (CommerceException error) { return error.Code; } }
        async Task<string> Void() { await using var db = database.Open(); try { await new VoidService(db, PaymentFixture.Commerce, PaymentFixture.Bank, new() { Enabled = true, ProfileApproved = true, WindowPolicy = VoidWindowPolicy.BankingDay2256 }, gateway, new(db, clock), clock).RequestAsync(fixture.OrderId, new(Key(), "Bank void"), maker, default); return "void"; } catch (CommerceException error) { return error.Code; } }
        var result = await Task.WhenAll(Manual(), Void()); Assert.Single(result, x => x is "manual" or "void"); Assert.Contains(result, x => x is "manual_refund_blocks_void" or "manual_refund_bank_void_conflict");
        await using var check = database.Open(); Assert.Equal(1, await check.ManualRefunds.CountAsync() + await check.Voids.CountAsync()); Assert.Equal(0, gateway.Calls);
    }

    [PgFact]
    public async Task Evidence_is_case_bound_deduplicated_private_and_hash_checked_before_download_or_approval()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (_, clock, bankId, amount) = await PaidAsync(database); var maker = Maker;
        await using var db = database.Open(); var service = Service(db, storage, clock); var first = await service.CreateAsync(bankId, new(Key(), Minor(amount / 2), "First partial case"), maker, default);
        var file = await ProofAsync(db, storage, clock, first.CaseId, maker); var duplicate = await ProofAsync(db, storage, clock, first.CaseId, maker); Assert.Equal(file.Id, duplicate.Id);
        Assert.Equal(0, await db.DigitalAssets.CountAsync()); Assert.Equal(1, await db.FinanceEvidence.CountAsync());
        var second = await service.CreateAsync(bankId, new(Key(), Minor(amount - amount / 2), "Second partial case"), maker, default);
        Assert.Equal("manual_refund_evidence_mismatch", (await Assert.ThrowsAsync<CommerceException>(() => service.ActAsync(second.Id, new(Key(), second.Version, "submit-evidence", "Wrong case proof", "EXT", clock.GetUtcNow(), [file.Id]), maker, default))).Code);
        var buyer = maker with { Roles = [] }; Assert.Equal("role_required", (await Assert.ThrowsAsync<CommerceException>(() => Evidence(db, storage, clock).DownloadAsync(file.Id, buyer, "test", default))).Code);
        var download = await Evidence(db, storage, clock).DownloadAsync(file.Id, maker, "test", default); await download.Stream.DisposeAsync();
        first = await service.GetAsync(first.Id, maker, default); first = await service.ActAsync(first.Id, new(Key(), first.Version, "submit-evidence", "Submit exact proof", "EXT", clock.GetUtcNow(), [file.Id]), maker, default);
        var record = await db.FinanceEvidence.AsNoTracking().SingleAsync(); await File.WriteAllTextAsync(storage.Files.PathFor("finance", record.Key, ".proof"), "corrupted");
        Assert.Equal("finance_evidence_unavailable", (await Assert.ThrowsAsync<CommerceException>(() => Evidence(db, storage, clock).DownloadAsync(file.Id, maker, "test", default))).Code);
        Assert.Equal("finance_evidence_unavailable", (await Assert.ThrowsAsync<CommerceException>(() => service.ActAsync(first.Id, new(Key(), first.Version, "approve-attestation", "Checked proof"), Checker, default))).Code);
        Assert.Equal("EvidenceSubmitted", (await service.GetAsync(first.Id, maker, default)).State);
    }

    [PgFact]
    public async Task Independent_approval_is_policy_gated_idempotent_and_full_primary_refund_preserves_bank_and_receipt_evidence()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, clock, bankId, amount) = await PaidAsync(database); var maker = Maker; var checker = Checker;
        await using var db = database.Open(); var receipts = new ReceiptService(db, new(db, clock), PaymentFixture.Commerce, new(), clock, storage.Files); await receipts.EnsureAsync(fixture.OrderId, default); var originalHash = (await db.Documents.SingleAsync()).SnapshotHash;
        var line = await db.OrderLines.SingleAsync(); db.Entitlements.Add(new Entitlement { OrderId = fixture.OrderId, OrderLineId = line.Id, ShopperId = fixture.ShopperId, State = EntitlementState.Active }); await db.SaveChangesAsync();
        var service = Service(db, storage, clock); var row = await service.CreateAsync(bankId, new(Key(), Minor(amount), "Full external refund"), maker, default); row = await SubmitAsync(db, storage, clock, row, maker);
        Assert.False(row.CanApprove); Assert.True((await service.GetAsync(row.Id, checker, default)).CanApprove);
        Assert.Equal("manual_refund_independent_review_required", (await Assert.ThrowsAsync<CommerceException>(() => service.ActAsync(row.Id, new(Key(), row.Version, "approve-attestation", "Self approval"), maker, default))).Code);
        Assert.Equal("manual_refund_policy_unapproved", (await Assert.ThrowsAsync<CommerceException>(() => Service(db, storage, clock, new()).ActAsync(row.Id, new(Key(), row.Version, "approve-attestation", "Policy absent"), checker, default))).Code);
        var input = new ManualRefundActionInput(Key(), row.Version, "approve-attestation", "Independent officer checked the external transfer evidence");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var context = database.Open(); return await Service(context, storage, clock).ActAsync(row.Id, input, checker, default); }));
        Assert.All(results, x => Assert.Equal("Attested", x.State)); Assert.Single(results.Select(x => x.Version).Distinct()); db.ChangeTracker.Clear();
        Assert.Equal(PaymentState.Refunded, (await db.Orders.SingleAsync()).PaymentState); Assert.Equal(OrderState.Review, (await db.Orders.SingleAsync()).State); Assert.False((await db.BankTransactions.SingleAsync()).Voided);
        Assert.Equal(EntitlementState.Revoked, (await db.Entitlements.SingleAsync()).State); Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Equal(originalHash, (await db.Documents.SingleAsync()).SnapshotHash);
        Assert.Equal(1, await db.Cases.CountAsync(x => x.Kind == "ManualRefundFulfillmentReview")); Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.Action == "manual-refund.approve-attestation"));
        Assert.Equal(1, await db.StockMovements.CountAsync(x => x.EffectKey.StartsWith("commit:"))); Assert.Equal(0, await db.Voids.CountAsync());
        await PaymentFixture.Processor(db, clock).ApplyAsync(BankWire.ParseNotice(fixture.Raw) with { ResultCode = "0000" }, fixture.Raw, PaymentFixture.Metadata(EvidenceSource.Verify), default);
        Assert.Equal(PaymentState.Refunded, (await db.Orders.AsNoTracking().SingleAsync()).PaymentState); var gateway = new NoNetworkGateway();
        await new VerificationService(db, PaymentFixture.Commerce, PaymentFixture.Bank, gateway, PaymentFixture.Processor(db, clock), clock).CheckAsync(fixture.ShopperId, fixture.OrderId, new(), default); Assert.Equal(0, gateway.Calls);
    }

    [PgFact]
    public async Task Rejected_paperwork_never_cancels_a_declared_transfer_or_releases_its_refund_budget()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (_, clock, bankId, amount) = await PaidAsync(database); var maker = Maker;
        await using var db = database.Open(); var service = Service(db, storage, clock); var row = await service.CreateAsync(bankId, new(Key(), Minor(amount), "External refund"), maker, default); row = await SubmitAsync(db, storage, clock, row, maker);
        row = await service.ActAsync(row.Id, new(Key(), row.Version, "reject-evidence", "The declaration needs clearer proof"), Checker, default); Assert.Equal("Review", row.State); Assert.NotNull(row.SubmittedAt);
        Assert.Equal("manual_refund_cannot_cancel", (await Assert.ThrowsAsync<CommerceException>(() => service.ActAsync(row.Id, new(Key(), row.Version, "cancel-before-submission", "Cannot release declared money"), maker, default))).Code);
        Assert.Equal("manual_refund_credit_exhausted", (await Assert.ThrowsAsync<CommerceException>(() => service.CreateAsync(bankId, new(Key(), "1", "Second refund"), maker, default))).Code);
    }

    [PgFact]
    public async Task Refunding_an_extra_credit_manually_keeps_primary_rights_and_requires_case_resolution_before_resuming()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, clock, _, amount) = await PaidAsync(database); var maker = Maker;
        await using var db = database.Open(); var raw = JsonSerializer.SerializeToUtf8Bytes(fixture.Notice with { InstructionId = "EXTERNAL-EXTRA-CREDIT" }, BankWire.Json);
        await PaymentFixture.Processor(db, clock).NoticeAsync(raw, PaymentFixture.Metadata(), default); var extra = await db.BankTransactions.SingleAsync(x => x.InstructionId == "EXTERNAL-EXTRA-CREDIT"); var source = await db.Cases.SingleAsync(x => x.Kind == "ExtraCredit");
        var line = await db.OrderLines.SingleAsync(); db.Entitlements.Add(new Entitlement { OrderId = fixture.OrderId, OrderLineId = line.Id, ShopperId = fixture.ShopperId, State = EntitlementState.Active }); await db.SaveChangesAsync();
        var service = Service(db, storage, clock); var row = await service.CreateAsync(extra.Id, new(Key(), Minor(amount), "Return only the extra credit", source.Id), maker, default); row = await SubmitAsync(db, storage, clock, row, maker);
        await service.ActAsync(row.Id, new(Key(), row.Version, "approve-attestation", "Independently reviewed extra-credit return"), Checker, default);
        var finance = new FinanceService(db, PaymentFixture.Commerce, PaymentFixture.Bank, clock);
        await finance.CaseActionAsync(source.Id, new(Key(), 1, "resolve-manual-refunded-credit", "Exact extra amount returned externally"), maker, default);
        await finance.CaseActionAsync(source.Id, new(Key(), 2, "resume-paid-order", "All extra-credit cases resolved; keep original purchase"), maker, default);
        Assert.Equal(PaymentState.Paid, (await db.Orders.SingleAsync()).PaymentState); Assert.Equal(OrderState.Open, (await db.Orders.SingleAsync()).State); Assert.Equal(EntitlementState.Active, (await db.Entitlements.SingleAsync()).State);
        Assert.False((await db.BankTransactions.SingleAsync(x => x.Id == extra.Id)).Voided); Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Equal(0, await db.Cases.CountAsync(x => x.Kind == "ManualRefundFulfillmentReview"));
        var result = await PaymentFixture.Processor(db, clock).ApplyAsync(BankWire.ParseNotice(raw) with { ResultCode = "0000" }, raw, PaymentFixture.Metadata(EvidenceSource.Verify), default);
        Assert.Equal("AlreadyExternallyRefunded", result.Outcome); Assert.Equal(OrderState.Open, (await db.Orders.SingleAsync()).State);
    }
}
