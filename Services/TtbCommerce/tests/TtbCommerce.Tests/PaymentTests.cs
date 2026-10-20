using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed record PaymentFixture(Guid ShopperId, Guid OrderId, Guid VariantId, CreditNotice Notice)
{
    public byte[] Raw => JsonSerializer.SerializeToUtf8Bytes(Notice, BankWire.Json);
    public static CommerceOptions Commerce => new() { Environment = CommerceEnvironment.Uat, PaymentMode = PaymentMode.Bank, Role = RuntimeRole.Api };
    public static TtbOptions Bank => new() { MerchantId = "TST", BillerNo = "111111111111111" };
    public static EvidenceMetadata Metadata(EvidenceSource source = EvidenceSource.Callback) => new(source, Guid.NewGuid().ToString("D"));
    public static PaymentProcessor Processor(CommerceDb db, TimeProvider? clock = null) => new(db, Commerce, Bank, clock ?? TimeProvider.System);

    public static async Task<PaymentFixture> CreateAsync(PgDatabase database)
    {
        await using var db = database.Open();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var actor = await CartTests.ShopperAsync(db, "alice-" + suffix);
        var variant = (await CatalogTests.PublishedAsync(db, "item-" + suffix)).Variant;
        var quote = await OrderTests.QuoteAsync(db, actor, (variant.Id, 1));
        var order = await OrderTests.Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "fixture", default);
        var local = new CommerceOptions(); var bank = new TtbOptions();
        var qr = await new QrService(db, new(db, TimeProvider.System), local, bank, new(local, bank), TimeProvider.System).GetOrCreateAsync(actor.ShopperId, order.Id, default);
        var attempt = await db.Attempts.SingleAsync(x => x.Id == qr.AttemptId);
        // Unit-only bank fixture: no real bank key, identity or external call. Replace every mock field explicitly.
        attempt.MerchantId = "TST"; attempt.BillerNo = Bank.BillerNo; attempt.QrId = attempt.QrId.Replace("ZSIM", "ZTST", StringComparison.Ordinal);
        attempt.Profile = "TTB-BPQR-ZE-v1";
        attempt.Payload = ThaiQr.Build(new(attempt.BillerNo, attempt.Ref1, attempt.Ref2, attempt.QrId, new Money(attempt.ExpectedMinor), "UNIT TEST"));
        attempt.PayloadHash = SecretTokens.Hash(attempt.Payload); await db.SaveChangesAsync();
        return new(actor.ShopperId, order.Id, variant.Id, new("TEST" + Guid.NewGuid().ToString("N"), attempt.BillerNo, attempt.Ref1, attempt.Ref2, attempt.QrId,
            new Money(attempt.ExpectedMinor).ToString(), "000", "Successful", DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), "9999999999", "ผู้ชำระทดสอบ", "0001"));
    }
}

public sealed class PaymentTests
{
    [PgFact]
    public async Task Concurrent_callbacks_commit_once_before_ACK_and_preserve_every_raw_delivery()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database); var raw = fixture.Raw;
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = database.Open(); return await PaymentFixture.Processor(db).NoticeAsync(raw, PaymentFixture.Metadata(), default);
        }));
        Assert.Single(results, x => x.Ack.ResCode == "0000"); Assert.Equal(11, results.Count(x => x.Ack.ResCode == "0004"));
        await using var check = database.Open();
        Assert.Equal(1, await check.BankTransactions.CountAsync()); Assert.Equal(1, await check.Allocations.CountAsync());
        Assert.Equal(12, await check.Observations.CountAsync()); Assert.Equal(12, (await check.CallbackReceipts.SingleAsync()).DeliveryCount);
        Assert.All(await check.Observations.ToArrayAsync(), x => Assert.Equal(raw, x.RawPayload));
        Assert.Equal(3, await check.Jobs.CountAsync()); // expiry + receipt + one fulfillment
        var stock = await check.Variants.SingleAsync(x => x.Id == fixture.VariantId); Assert.Equal(4, stock.OnHand); Assert.Equal(0, stock.Reserved);
        Assert.Equal(PaymentState.Paid, (await check.Orders.SingleAsync()).PaymentState);
        Assert.Equal(ReservationState.Committed, (await check.Reservations.SingleAsync()).State);
    }

    [PgFact]
    public async Task Verify_before_callback_is_not_already_acknowledged_and_optional_Ref2_can_be_enriched()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        var evidence = BankWire.ParseNotice(fixture.Raw) with { ResultCode = "0000", Ref2 = null };
        var verifyRaw = JsonSerializer.SerializeToUtf8Bytes(fixture.Notice with { ResultCode = "0000", Ref2 = null }, BankWire.Json);
        await using (var db = database.Open()) await PaymentFixture.Processor(db).ApplyAsync(evidence, verifyRaw, PaymentFixture.Metadata(EvidenceSource.Verify), default);
        await using (var db = database.Open()) Assert.Equal("0000", (await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default)).Ack.ResCode);
        await using (var db = database.Open()) Assert.Equal("0004", (await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default)).Ack.ResCode);
        await using (var db = database.Open())
        {
            Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Equal(1, await db.StockMovements.CountAsync(x => x.EffectKey.StartsWith("commit:")));
            Assert.Equal(fixture.Notice.Ref2, (await db.BankTransactions.SingleAsync()).Ref2);
            Assert.Null((await db.BankTransactions.SingleAsync()).PaymentOccurredAt); // TransDate is documented system time
        }
    }

    [PgFact]
    public async Task Callback_and_verify_race_cannot_allocate_twice()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        await Task.WhenAll(Task.Run(async () =>
        {
            await using var db = database.Open(); await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default);
        }), Task.Run(async () =>
        {
            await using var db = database.Open(); await PaymentFixture.Processor(db).ApplyAsync(BankWire.ParseNotice(fixture.Raw) with { ResultCode = "0000" }, fixture.Raw, PaymentFixture.Metadata(EvidenceSource.Verify), default);
        }));
        await using var check = database.Open(); Assert.Equal(1, await check.Allocations.CountAsync()); Assert.Equal(3, await check.Jobs.CountAsync()); Assert.Equal(2, await check.Observations.CountAsync());
    }

    [PgFact]
    public async Task A_bank_result_cannot_confirm_an_attempt_created_by_the_mock_profile()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        await using (var db = database.Open()) await db.Attempts.ExecuteUpdateAsync(s => s.SetProperty(x => x.Profile, "MOCK-v1"));
        await using (var db = database.Open())
        {
            var evidence = BankWire.ParseNotice(fixture.Raw) with { ResultCode = "0000" };
            Assert.Equal("AttemptProfileMismatch", (await PaymentFixture.Processor(db).ApplyAsync(evidence, fixture.Raw, PaymentFixture.Metadata(EvidenceSource.Verify), default)).Outcome);
        }
        await using var check = database.Open(); Assert.Equal(0, await check.Allocations.CountAsync());
    }

    [PgTheory]
    [InlineData("Amount", "0005")]
    [InlineData("Ref1", "0006")]
    [InlineData("Ref2", "0007")]
    [InlineData("Biller", "0003")]
    [InlineData("QRId", "0002")]
    public async Task Mismatch_is_retained_for_reconciliation_and_cannot_release_goods(string field, string expectedCode)
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        var notice = field switch
        {
            "Amount" => fixture.Notice with { Amount = "1.00" }, "Ref1" => fixture.Notice with { Ref1 = "BADREF" }, "Ref2" => fixture.Notice with { Ref2 = "BADREF" },
            "Biller" => fixture.Notice with { BillerNo = "222222222222222" }, _ => fixture.Notice with { QrId = fixture.Notice.QrId![..15] + "99999" }
        };
        await using (var db = database.Open())
        {
            var result = await PaymentFixture.Processor(db).NoticeAsync(JsonSerializer.SerializeToUtf8Bytes(notice, BankWire.Json), PaymentFixture.Metadata(), default);
            Assert.Equal(expectedCode, result.Ack.ResCode);
        }
        await using var check = database.Open(); Assert.Equal(0, await check.Allocations.CountAsync()); Assert.Equal(1, await check.BankTransactions.CountAsync()); Assert.Equal(1, await check.Cases.CountAsync());
        Assert.NotEqual(PaymentState.Paid, (await check.Orders.SingleAsync()).PaymentState); Assert.Equal(5, (await check.Variants.SingleAsync()).OnHand);
    }

    [PgFact]
    public async Task Extra_credit_and_changed_duplicate_are_cases_without_extra_fulfillment()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        await using (var db = database.Open()) await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default);
        await using (var db = database.Open())
        {
            var changed = JsonSerializer.SerializeToUtf8Bytes(fixture.Notice with { Amount = "2.00" }, BankWire.Json);
            Assert.Equal("0001", (await PaymentFixture.Processor(db).NoticeAsync(changed, PaymentFixture.Metadata(), default)).Ack.ResCode);
        }
        await using (var db = database.Open()) Assert.Equal(OrderState.Review, (await db.Orders.SingleAsync()).State);
        await using (var db = database.Open())
        {
            var extra = JsonSerializer.SerializeToUtf8Bytes(fixture.Notice with { InstructionId = "ANOTHER-INSTRUCTION" }, BankWire.Json);
            var result = await PaymentFixture.Processor(db).NoticeAsync(extra, PaymentFixture.Metadata(), default);
            Assert.Equal("0000", result.Ack.ResCode); Assert.Equal("ExtraCredit", result.Outcome);
        }
        await using var check = database.Open(); Assert.Equal(2, await check.BankTransactions.CountAsync()); Assert.Equal(1, await check.Allocations.CountAsync()); Assert.Equal(3, await check.Jobs.CountAsync());
        Assert.Equal(2, await check.Cases.CountAsync()); Assert.Equal(4, (await check.Variants.SingleAsync()).OnHand);
    }

    [PgFact]
    public async Task Payment_after_cancellation_and_unapproved_cross_border_are_reviewed_without_delivery()
    {
        await using var database = await PgDatabase.CreateAsync(); var late = await PaymentFixture.CreateAsync(database); var cross = await PaymentFixture.CreateAsync(database);
        await using (var db = database.Open()) await OrderTests.Service(db).CancelAsync(late.ShopperId, late.OrderId, default);
        await using (var db = database.Open()) Assert.Equal("LateOrClosedOrder", (await PaymentFixture.Processor(db).NoticeAsync(late.Raw, PaymentFixture.Metadata(), default)).Outcome);
        await using (var db = database.Open())
        {
            var raw = JsonSerializer.SerializeToUtf8Bytes(cross.Notice with { CrossBorderFlag = "Y", SwitchCode = "156000", CurrencyCode = "CNY", SwitchFullName = "UNIONPAY" }, BankWire.Json);
            Assert.Equal("CrossBorderReview", (await PaymentFixture.Processor(db).NoticeAsync(raw, PaymentFixture.Metadata(), default)).Outcome);
        }
        await using var check = database.Open(); Assert.Equal(0, await check.Allocations.CountAsync()); Assert.Equal(2, await check.BankTransactions.CountAsync()); Assert.Equal(2, await check.Cases.CountAsync());
    }

    [PgFact]
    public async Task Invalid_JSON_duplicate_fields_and_NUL_are_preserved_as_exact_bytes()
    {
        await using var database = await PgDatabase.CreateAsync();
        var values = new[] { new byte[] { 0xFF, 0, 0x7B }, Encoding.UTF8.GetBytes("{\"InstructionId\":\"one\",\"InstructionId\":\"two\"}"), Encoding.UTF8.GetBytes("{\"InstructionId\":\"bad\\u0000id\"}") };
        foreach (var raw in values)
        {
            await using var db = database.Open(); Assert.Equal("0001", (await PaymentFixture.Processor(db).NoticeAsync(raw, PaymentFixture.Metadata(), default)).Ack.ResCode);
        }
        await using var check = database.Open(); var observations = await check.Observations.ToArrayAsync();
        Assert.Equal(3, observations.Length); foreach (var value in values) Assert.Contains(observations, x => x.RawPayload.SequenceEqual(value));
        Assert.Equal(0, await check.BankTransactions.CountAsync());
    }

    [PgFact]
    public async Task Persistence_failure_cannot_return_success_or_leave_partial_payment_stock_or_jobs()
    {
        await using var database = await PgDatabase.CreateAsync(); var fixture = await PaymentFixture.CreateAsync(database);
        await using (var db = database.Open())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION ttb.reject_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'injected persistence failure'; END; $$;
                CREATE TRIGGER reject_receipt BEFORE INSERT ON ttb."CallbackReceipts" FOR EACH ROW EXECUTE FUNCTION ttb.reject_receipt();
                """);
            await Assert.ThrowsAsync<DbUpdateException>(() => PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default));
        }
        await using var check = database.Open(); Assert.Equal(0, await check.BankTransactions.CountAsync()); Assert.Equal(0, await check.Allocations.CountAsync()); Assert.Equal(0, await check.Observations.CountAsync());
        Assert.Equal(1, await check.Jobs.CountAsync()); Assert.Equal(PaymentState.Unpaid, (await check.Orders.SingleAsync()).PaymentState);
        var stock = await check.Variants.SingleAsync(); Assert.Equal(5, stock.OnHand); Assert.Equal(1, stock.Reserved);
    }
}
