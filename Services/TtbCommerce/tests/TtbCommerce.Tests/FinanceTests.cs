using System.Net;
using System.Net.Http.Json;
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

public sealed class FinanceTests
{
    private static ShopperActor Finance => new(Guid.NewGuid(), "mangosgo", "finance", "Finance Test", ["finance"]);
    private static FinanceService Service(CommerceDb db) => new(db, PaymentFixture.Commerce, PaymentFixture.Bank, TimeProvider.System);
    private static async Task<(PaymentFixture Fixture, Guid CaseId)> LateAsync(PgDatabase database)
    {
        var fixture = await PaymentFixture.CreateAsync(database); await using var db = database.Open();
        await OrderTests.Service(db).CancelAsync(fixture.ShopperId, fixture.OrderId, default);
        Assert.Equal("LateOrClosedOrder", (await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default)).Outcome);
        return (fixture, await db.Cases.Where(x => x.Kind == "LateOrClosedOrder").Select(x => x.Id).SingleAsync());
    }

    [PgFact]
    public async Task Explicit_late_credit_acceptance_rechecks_released_stock_and_concurrent_retries_allocate_once()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, id) = await LateAsync(database); var actor = Finance;
        var input = new CaseActionInput(Guid.NewGuid().ToString("D"), 1, "accept-late-payment", "Buyer confirmed purchase after QR expiry; available stock checked");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); return await Service(db).CaseActionAsync(id, input, actor, default); }));
        Assert.Single(results.Select(x => x.OperationId).Distinct()); await using var check = database.Open();
        Assert.Equal(PaymentState.Paid, (await check.Orders.SingleAsync()).PaymentState); Assert.Equal(OrderState.Open, (await check.Orders.SingleAsync()).State);
        Assert.Equal(1, await check.Allocations.CountAsync()); Assert.Equal(ReservationState.Committed, (await check.Reservations.SingleAsync()).State);
        Assert.Equal(1, await check.StockMovements.CountAsync(x => x.EffectKey.StartsWith("commit:"))); Assert.Equal(0, (await check.Variants.SingleAsync()).Reserved);
        Assert.Equal(1, await check.Jobs.CountAsync(x => x.Kind == "receipt.ensure")); Assert.Equal(1, await check.Jobs.CountAsync(x => x.Kind == "fulfillment.dispatch"));
        Assert.Equal("Resolved", (await check.Cases.SingleAsync()).State); Assert.Equal(1, await check.AuditEvents.CountAsync(x => x.Action == "finance.payment-recovery"));
    }

    [PgFact]
    public async Task Late_recovery_cannot_oversell_consume_other_reserved_stock_or_override_an_identity_conflict()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, id) = await LateAsync(database); await using var db = database.Open(); var actor = Finance;
        var input = new CaseActionInput(Guid.NewGuid().ToString("D"), 1, "accept-late-payment", "Explicit late acceptance");
        await db.Variants.Where(x => x.Id == fixture.VariantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.OnHand));
        Assert.Equal("insufficient_stock", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).CaseActionAsync(id, input, actor, default))).Code);
        db.ChangeTracker.Clear(); Assert.Equal(0, await db.Allocations.CountAsync()); Assert.Equal("Open", (await db.Cases.SingleAsync()).State); Assert.Equal(ReservationState.Released, (await db.Reservations.SingleAsync()).State);
        await db.Variants.Where(x => x.Id == fixture.VariantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, 0));
        db.Cases.Add(new ReconciliationCase { OrderId = fixture.OrderId, Kind = "InstructionConflict", EffectKey = "test-conflict" }); await db.SaveChangesAsync();
        Assert.Equal("payment_recovery_ineligible", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).CaseActionAsync(id, input, actor, default))).Code);
        Assert.Equal(0, await db.FinanceOperations.CountAsync()); Assert.Equal(0, await db.Allocations.CountAsync());
    }

    [PgFact]
    public async Task Extra_credit_case_needs_confirmed_void_and_resolved_holds_before_primary_delivery_can_resume()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database); await using var db = database.Open();
        await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default);
        var extraRaw = JsonSerializer.SerializeToUtf8Bytes(fixture.Notice with { InstructionId = "SECOND-CREDIT" }, BankWire.Json);
        await PaymentFixture.Processor(db).NoticeAsync(extraRaw, PaymentFixture.Metadata(), default); var review = await db.Cases.SingleAsync(x => x.Kind == "ExtraCredit"); var actor = Finance;
        var close = new CaseActionInput(Guid.NewGuid().ToString("D"), review.Version, "resolve-voided-credit", "Bank confirmed return of the duplicate credit");
        Assert.Equal("case_void_evidence_required", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).CaseActionAsync(review.Id, close, actor, default))).Code);
        await db.BankTransactions.Where(x => x.InstructionId == "SECOND-CREDIT").ExecuteUpdateAsync(s => s.SetProperty(x => x.Voided, true));
        await Service(db).CaseActionAsync(review.Id, close, actor, default);
        db.Cases.Add(new ReconciliationCase { OrderId = fixture.OrderId, Kind = "NotificationPayloadChanged", EffectKey = "test-metadata" }); await db.SaveChangesAsync();
        var resume = new CaseActionInput(Guid.NewGuid().ToString("D"), 2, "resume-paid-order", "Duplicate returned; keep original purchase");
        Assert.Equal("order_resume_ineligible", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).CaseActionAsync(review.Id, resume, actor, default))).Code);
        db.ChangeTracker.Clear(); var metadata = await db.Cases.SingleAsync(x => x.Kind == "NotificationPayloadChanged");
        await Service(db).CaseActionAsync(metadata.Id, new(Guid.NewGuid().ToString("D"), metadata.Version, "dismiss-metadata", "Same payment; harmless sender metadata"), actor, default);
        await Service(db).CaseActionAsync(review.Id, resume, actor, default); Assert.Equal(OrderState.Open, (await db.Orders.SingleAsync()).State);
        Assert.Equal(PaymentState.Paid, (await db.Orders.SingleAsync()).PaymentState); Assert.False((await db.BankTransactions.SingleAsync(x => x.InstructionId != "SECOND-CREDIT")).Voided);
        Assert.Equal(1, await db.Allocations.CountAsync());
    }
    private static async Task<(PaymentFixture Fixture, Guid CaseId)> MismatchAsync(PgDatabase database)
    {
        var fixture = await PaymentFixture.CreateAsync(database); fixture = fixture with { Notice = fixture.Notice with { Amount = "0.01" } };
        await using var db = database.Open(); await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default);
        return (fixture, await db.Cases.Select(x => x.Id).SingleAsync());
    }

    [PgFact]
    public async Task Concurrent_note_retries_are_one_version_one_note_one_audited_operation_without_changing_money()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, id) = await MismatchAsync(database); var actor = Finance;
        var input = new CaseActionInput(Guid.NewGuid().ToString("D"), 1, "note", "ตรวจสอบหลักฐานกับธนาคาร\nยังไม่ยืนยันรับยอด");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => { await using var db = database.Open(); return await Service(db).CaseActionAsync(id, input, actor, default); }));
        Assert.Single(results.Select(x => x.OperationId).Distinct()); Assert.All(results, x => Assert.Equal(2, x.Version));
        await using var check = database.Open(); Assert.Equal(1, await check.CaseNotes.CountAsync()); Assert.Equal(1, await check.FinanceOperations.CountAsync());
        Assert.Equal(0, await check.Allocations.CountAsync()); Assert.Equal(PaymentState.Review, (await check.Orders.SingleAsync(x => x.Id == fixture.OrderId)).PaymentState);
        Assert.Equal(1, await check.AuditEvents.CountAsync(x => x.Action == "finance.case.note"));
        Assert.Equal("operation_conflict", (await Assert.ThrowsAsync<CommerceException>(() => Service(check).CaseActionAsync(id, input with { Reason = "different" }, actor, default))).Code);
    }

    [PgFact]
    public async Task Two_officers_cannot_overwrite_the_same_version_and_a_note_cannot_set_paid_or_dismiss_a_financial_discrepancy()
    {
        await using var database = await PgDatabase.CreateAsync(); var (_, id) = await MismatchAsync(database);
        var outcomes = await Task.WhenAll(new[] { "claim", "note" }.Select(async action =>
        {
            await using var db = database.Open();
            try { await Service(db).CaseActionAsync(id, new(Guid.NewGuid().ToString("D"), 1, action, "Review"), Finance, default); return "success"; }
            catch (CommerceException exception) { return exception.Code; }
        }));
        Assert.Single(outcomes, x => x == "success"); Assert.Single(outcomes, x => x == "case_changed");
        await using var check = database.Open(); var current = await check.Cases.AsNoTracking().SingleAsync(); Assert.Equal(2, current.Version);
        Assert.Equal("case_financial_resolution_required", (await Assert.ThrowsAsync<CommerceException>(() => Service(check).CaseActionAsync(id, new(Guid.NewGuid().ToString("D"), 2, "dismiss-metadata", "Close"), Finance, default))).Code);
        Assert.Equal("case_action_invalid", (await Assert.ThrowsAsync<CommerceException>(() => Service(check).CaseActionAsync(id, new(Guid.NewGuid().ToString("D"), 2, "set-paid", "Close"), Finance, default))).Code);
        Assert.Equal(0, await check.Allocations.CountAsync()); Assert.Equal(1, await check.CaseNotes.CountAsync());
    }

    [PgFact]
    public async Task Report_void_evidence_resolves_uncertainty_with_a_version_and_audit_but_preserves_fulfillment_review()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, bankId) = await ReportFixtures.PaidAsync(database);
        Guid operationId; Guid caseId;
        await using (var db = database.Open())
        {
            var operation = new VoidOperation { Environment = CommerceEnvironment.Uat, MerchantId = "TST", OrderId = fixture.OrderId, BankTransactionId = bankId,
                InstructionId = fixture.Notice.InstructionId!, QrId = fixture.Notice.QrId!, BillerNo = fixture.Notice.BillerNo!, TransactionId = "TST20261018100000", OperationKey = Guid.NewGuid().ToString("D"), State = "Unknown", SendFencedAt = DateTimeOffset.UtcNow, AmountMinor = Money.Parse(fixture.Notice.Amount!).Minor };
            var review = new ReconciliationCase { OrderId = fixture.OrderId, BankTransactionId = bankId, Kind = "VoidOperationReview", EffectKey = $"void-review:{operation.Id}" };
            db.Voids.Add(operation); db.Cases.Add(review); await db.SaveChangesAsync(); operationId = operation.Id; caseId = review.Id;
        }
        var row = ReportFixtures.From(fixture, "D", "VOIDTEST", fixture.Notice.InstructionId!); var plain = ReportFixtures.File(ReportFixtures.Profile(), ReportKind.Iso, row);
        await ReportFixtures.StageAsync(database, storage, plain);
        await using (var db = database.Open())
        {
            await ReportFixtures.Service(db, storage, plain).ReconcileAsync(await db.ReportRows.Select(x => x.Id).SingleAsync(), default);
            Assert.Equal("Confirmed", (await db.Voids.SingleAsync(x => x.Id == operationId)).State); var resolved = await db.Cases.SingleAsync(x => x.Id == caseId);
            Assert.Equal("Resolved", resolved.State); Assert.Equal(2, resolved.Version); Assert.Contains("ReportD", resolved.ResolutionJson);
            Assert.Equal(1, await db.Cases.CountAsync(x => x.Kind == "ReportedVoidFulfillmentReview" && x.State == "Open"));
            Assert.Equal("case_changed", (await Assert.ThrowsAsync<CommerceException>(() => Service(db).CaseActionAsync(caseId, new(Guid.NewGuid().ToString("D"), 1, "claim", "Old screen"), Finance, default))).Code);
        }
    }

    private sealed class FinanceApiFactory(string connection) : CommerceApiFactory(connection, enableLocal: false, environment: CommerceEnvironment.Uat)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Commerce:FulfillmentSubjects:0"] = "mangosgo:warehouse" }));
            builder.ConfigureServices(services => { services.RemoveAll<TtbOptions>(); services.AddSingleton(PaymentFixture.Bank); });
        }
    }

    [PgFact]
    public async Task Finance_HTTP_is_role_scoped_private_exact_for_large_amounts_and_missing_reports_are_not_zero_settlement()
    {
        await using var database = await PgDatabase.CreateAsync(); var (fixture, id) = await MismatchAsync(database);
        await using (var db = database.Open()) await db.BankTransactions.ExecuteUpdateAsync(s => s.SetProperty(x => x.AmountMinor, 9007199254740993L));
        await using var factory = new FinanceApiFactory(database.ConnectionString); using var buyer = factory.ShopperClient("buyer"); using var admin = factory.ShopperClient("admin"); using var warehouse = factory.ShopperClient("warehouse");
        await CartTests.SignInAsync(buyer); await CartTests.SignInAsync(admin); await CartTests.SignInAsync(warehouse);
        var path = $"/ttb-commerce/v1/admin/reconciliation/{id}";
        buyer.DefaultRequestHeaders.Add("X-Role", "admin"); Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await warehouse.GetAsync($"/ttb-commerce/v1/admin/orders/{fixture.OrderId}")).StatusCode);
        var response = await admin.GetAsync(path); Assert.True(response.Headers.CacheControl?.NoStore); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<FinanceCaseDetail>(); Assert.Equal("9007199254740993", detail!.Bank!.AmountMinor);
        Assert.Single((await admin.GetFromJsonAsync<FinanceCasePage>($"/ttb-commerce/v1/admin/reconciliation?orderId={fixture.OrderId}"))!.Items);
        Assert.Empty((await admin.GetFromJsonAsync<FinanceCasePage>($"/ttb-commerce/v1/admin/reconciliation?orderId={Guid.NewGuid()}"))!.Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync("/ttb-commerce/v1/admin/orders")).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-Token"); Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(path + "/actions", new CaseActionInput(Guid.NewGuid().ToString("D"), 1, "note", "Review"))).StatusCode);
        var summary = await admin.GetFromJsonAsync<JsonElement>("/ttb-commerce/v1/admin/finance/summary"); Assert.Equal("Unavailable", summary.GetProperty("reportsState").GetString()); Assert.Equal(JsonValueKind.Null, summary.GetProperty("waitingFiles").ValueKind);
    }
}
