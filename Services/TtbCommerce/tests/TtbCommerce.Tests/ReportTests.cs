using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

internal static class ReportFixtures
{
    public const string IsoName = "BPQR_ISO_261017.TXT";
    public const string LostName = "BPQRLOST_261017.TXT";
    public static ReportOptions Profile(ReportWidth width = ReportWidth.Utf8Bytes, ReportLayout layout = ReportLayout.CrossBorder301) => new()
    {
        ProfileApproved = true, Layout = layout, Width = width, CreditTotal = ReportCreditTotal.IncludesVoidedOriginals,
        FileDateIsHeaderDate = true, MerchantAccount = "1234567890", DailyScope = ReportDailyScope.CalendarDay
    };
    public static ReportDetail Detail(string instruction = "20261017111111111111111111111111111", string kind = "C", string original = "") =>
        new(2, instruction, original, "004", "9999999999", "Test Buyer", "นาย ทดสอบ", "1234567890",
            DateTimeOffset.Parse("2026-10-17T10:11:12+07:00", CultureInfo.InvariantCulture), "ORDER1", "SHOP1", "ZTSTE261017235900001", "", kind, "N", 11175, 25, "111111111111111", null, "N", "", "", "");

    // An independent specification fixture writer; the production parser has no dependency on this helper.
    public static byte[] File(ReportOptions profile, ReportKind kind, params ReportDetail[] rows)
    {
        byte[] Line(params (int Position, int Width, string Value)[] values)
        {
            var result = Enumerable.Repeat((byte)' ', 500).ToArray(); var characters = Enumerable.Repeat(" ", 500).ToArray();
            foreach (var (position, width, value) in values)
            {
                var bytes = Encoding.UTF8.GetBytes(value); var runes = value.EnumerateRunes().Select(x => x.ToString()).ToArray();
                if ((profile.Width == ReportWidth.Utf8Bytes ? bytes.Length : runes.Length) > width) throw new InvalidOperationException("Fixture field too long.");
                if (profile.Width == ReportWidth.Utf8Bytes) bytes.CopyTo(result, position - 1);
                else Array.Copy(runes, 0, characters, position - 1, runes.Length);
            }
            return profile.Width == ReportWidth.Utf8Bytes ? result : Encoding.UTF8.GetBytes(string.Concat(characters));
        }
        var lines = new List<byte[]> { Line((1, 1, "H"), (2, 6, "000001"), (8, 3, "TST"), (11, 8, "17102026")) };
        var sequence = 1;
        foreach (var row in rows)
        {
            var fields = new List<(int, int, string)>
            {
                (1,1,"D"), (2,6,(++sequence).ToString("D6",CultureInfo.InvariantCulture)), (8,35,row.InstructionId), (43,35,row.OriginalInstructionId),
                (78,3,row.PayerBank), (81,20,row.PayerAccount), (101,40,row.PayerName), (141,40,row.PayerNameThai), (181,20,row.MerchantAccount),
                (201,8,row.PaymentAt.ToString("ddMMyyyy",CultureInfo.InvariantCulture)), (209,6,row.PaymentAt.ToString("HHmmss",CultureInfo.InvariantCulture)),
                (215,20,row.Ref1),(235,20,row.Ref2),(255,20,row.QrId),(275,4,row.Teller),(279,1,row.TransactionKind),(280,1,row.LumpsumFlag),
                (281,15,row.AmountMinor.ToString("D15",CultureInfo.InvariantCulture)),(296,4,row.FeeMinor.ToString("D4",CultureInfo.InvariantCulture)),(300,20,row.BillerNo)
            };
            if (kind == ReportKind.Lost)
            {
                var credit = row.CreditAt ?? row.PaymentAt.AddDays(1);
                fields.Add((320,8,credit.ToString("ddMMyyyy",CultureInfo.InvariantCulture))); fields.Add((328,6,credit.ToString("HHmmss",CultureInfo.InvariantCulture)));
            }
            var start = kind == ReportKind.Lost ? 334 : 320;
            if (profile.Layout == ReportLayout.CrossBorder301)
                fields.AddRange([(start,1,row.CrossBorderFlag),(start+1,6,row.SwitchCode),(start+7,3,row.CurrencyCode),(start+10,100,row.SwitchFullName)]);
            lines.Add(Line(fields.ToArray()));
        }
        var debits = rows.Where(x => x.TransactionKind == "D").ToArray();
        var credits = rows.Where(x => x.TransactionKind == "C" || profile.CreditTotal == ReportCreditTotal.IncludesVoidedOriginals && x.TransactionKind == "V").ToArray();
        lines.Add(Line((1,1,"T"),(2,6,(profile.SequenceIncludesTrailer ? ++sequence : sequence).ToString("D6",CultureInfo.InvariantCulture)),
            (8,15,debits.Sum(x=>x.AmountMinor).ToString("D15",CultureInfo.InvariantCulture)),(23,6,debits.Length.ToString("D6",CultureInfo.InvariantCulture)),
            (29,15,credits.Sum(x=>x.AmountMinor).ToString("D15",CultureInfo.InvariantCulture)),(44,6,credits.Length.ToString("D6",CultureInfo.InvariantCulture))));
        using var output = new MemoryStream(); foreach (var line in lines) { output.Write(line); output.Write("\r\n"u8); } return output.ToArray();
    }
    public static ReportDetail From(PaymentFixture fixture, string kind = "C", string? instruction = null, string original = "") => Detail(instruction ?? fixture.Notice.InstructionId!, kind, original) with
    { QrId = fixture.Notice.QrId!, Ref1 = fixture.Notice.Ref1!, Ref2 = fixture.Notice.Ref2!, AmountMinor = Money.Parse(fixture.Notice.Amount!).Minor };

    public sealed class FakeDecryptor(byte[] plain) : IReportDecryptor { public byte[] Decrypt(byte[] encrypted, ReportCryptoProfile? archiveProfile = null) => plain.ToArray(); }
    public static ReportService Service(CommerceDb db, TestPrivateStorage storage, byte[] plain, ReportOptions? options = null, TimeProvider? clock = null) =>
        new(db, PaymentFixture.Commerce, PaymentFixture.Bank, options ?? Profile(), storage.Files, new FakeDecryptor(plain), clock ?? TimeProvider.System);
    public static async Task<(PaymentFixture Fixture, Guid BankId)> PaidAsync(PgDatabase database)
    {
        var fixture = await PaymentFixture.CreateAsync(database);
        fixture = fixture with { Notice = fixture.Notice with { InstructionId = "TST" + Guid.NewGuid().ToString("N") } }; // exactly35, no truncation
        await using var db = database.Open(); var payment = await PaymentFixture.Processor(db).NoticeAsync(fixture.Raw, PaymentFixture.Metadata(), default);
        return (fixture, payment.BankTransactionId!.Value);
    }
    public static async Task<Guid> StageAsync(PgDatabase database, TestPrivateStorage storage, byte[] plain, byte[]? cipher = null, string fileName = IsoName)
    {
        await using var db = database.Open(); var service = Service(db, storage, plain); var archive = await service.ImportAsync(fileName, cipher ?? RandomNumberGenerator.GetBytes(80), default);
        await service.StageAsync(archive.Id, false, default); return archive.Id;
    }
}

public sealed class ReportParserTests
{
    [Theory]
    [InlineData(ReportWidth.Utf8Bytes, ReportLayout.CrossBorder301, ReportKind.Iso)]
    [InlineData(ReportWidth.Utf8Bytes, ReportLayout.CrossBorder301, ReportKind.Lost)]
    [InlineData(ReportWidth.UnicodeScalars, ReportLayout.CrossBorder301, ReportKind.Iso)]
    [InlineData(ReportWidth.UnicodeScalars, ReportLayout.CrossBorder301, ReportKind.Lost)]
    [InlineData(ReportWidth.Utf8Bytes, ReportLayout.Legacy238, ReportKind.Iso)]
    [InlineData(ReportWidth.UnicodeScalars, ReportLayout.Legacy238, ReportKind.Lost)]
    public void Thai_fields_and_independent_ISO_LOST_offsets_require_explicit_profile(ReportWidth width, ReportLayout layout, ReportKind kind)
    {
        var profile = ReportFixtures.Profile(width, layout); var row = ReportFixtures.Detail();
        if (layout == ReportLayout.CrossBorder301) row = row with { CrossBorderFlag = "Y", SwitchCode = "156000", CurrencyCode = "CNY", SwitchFullName = "UNIONPAY INTERNATIONAL CO.,LTD." };
        var parsed = ReportParser.Parse(ReportFixtures.File(profile, kind, row), kind == ReportKind.Iso ? ReportFixtures.IsoName : ReportFixtures.LostName, profile);
        var result = Assert.Single(parsed.Details);
        Assert.Equal("นาย ทดสอบ", result.PayerNameThai); Assert.Equal(11175, result.AmountMinor); Assert.Equal(25, result.FeeMinor); Assert.Equal(row.PaymentAt, result.PaymentAt);
        Assert.Equal(kind == ReportKind.Lost ? row.PaymentAt.AddDays(1) : (DateTimeOffset?)null, result.CreditAt);
        Assert.Equal(layout == ReportLayout.CrossBorder301 ? "CNY" : "", result.CurrencyCode);
    }

    [Fact]
    public void V_is_an_original_marker_and_never_a_second_debit_and_trailer_convention_is_explicit()
    {
        var profile = ReportFixtures.Profile(); var original = ReportFixtures.Detail(kind: "V"); var debit = original with { TransactionKind = "D", InstructionId = "VOID1", OriginalInstructionId = original.InstructionId };
        var parsed = ReportParser.Parse(ReportFixtures.File(profile, ReportKind.Iso, original, debit), ReportFixtures.IsoName, profile);
        Assert.Equal(11175, parsed.Totals.DebitMinor); Assert.Equal(1, parsed.Totals.DebitCount); Assert.Equal(11175, parsed.Totals.CreditMinor); Assert.Equal(1, parsed.Totals.VoidedOriginalCount);
        profile.CreditTotal = ReportCreditTotal.ExcludesVoidedOriginals;
        parsed = ReportParser.Parse(ReportFixtures.File(profile, ReportKind.Iso, original, debit), ReportFixtures.IsoName, profile);
        Assert.Equal(0, parsed.Totals.CreditMinor); Assert.Equal(11175, parsed.Totals.DebitMinor);
    }

    [Theory]
    [InlineData("width", "report_record_width")]
    [InlineData("sequence", "report_sequence")]
    [InlineData("total", "report_totals")]
    [InlineData("date", "report_file_date")]
    [InlineData("utf8", "report_utf8")]
    [InlineData("duplicate", "report_duplicate_movement")]
    public void Whole_file_is_rejected_before_any_rows_can_be_applied(string mutation, string expected)
    {
        var profile = ReportFixtures.Profile(); var row = ReportFixtures.Detail();
        var bytes = ReportFixtures.File(profile, ReportKind.Iso, mutation == "duplicate" ? [row, row] : [row]);
        var name = ReportFixtures.IsoName;
        if (mutation == "width") bytes = bytes[1..];
        if (mutation == "sequence") bytes[503] = (byte)'9';
        if (mutation == "total") bytes[2 * 502 + 42] = (byte)'4';
        if (mutation == "date") name = "BPQR_ISO_261016.TXT";
        if (mutation == "utf8") bytes[600] = 0xFF;
        Assert.Equal(expected, Assert.Throws<CommerceException>(() => ReportParser.Parse(bytes, name, profile)).Code);
    }

    [Fact]
    public void Unknown_profile_layout_mismatch_and_bad_filenames_are_not_auto_detected()
    {
        var profile = ReportFixtures.Profile(); var bytes = ReportFixtures.File(profile, ReportKind.Iso, ReportFixtures.Detail());
        Assert.Equal("report_profile_unapproved", Assert.Throws<CommerceException>(() => ReportParser.Parse(bytes, ReportFixtures.IsoName, new())).Code);
        profile.Layout = ReportLayout.Legacy238;
        Assert.Equal("report_reserved_field", Assert.Throws<CommerceException>(() => ReportParser.Parse(bytes, ReportFixtures.IsoName, profile)).Code);
        foreach (var name in new[] { "../BPQR_ISO_261017.TXT", "BPQR_ISO_261032.TXT", "BPQR_ISO_261017.TXT.pgp", "BPQR_ISO_261017.TXT\n" })
            Assert.Throws<CommerceException>(() => ReportParser.FileName(name));
        Assert.NotEmpty(new ReportOptions().Missing());
        Assert.Contains("Reports.ProductionMandatoryProfile", new BankReadiness(new() { Environment = CommerceEnvironment.Production, PaymentMode = PaymentMode.Bank }, new()).Missing());
        var hostKey = RandomNumberGenerator.GetBytes(48); var pin = "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');
        Assert.True(TtbReportSource.TrustedHostKey(hostKey, pin)); Assert.False(TtbReportSource.TrustedHostKey(hostKey, "SHA256:" + new string('A', 43)));
    }
}

public sealed class ReportIntegrationTests
{
    [PgFact]
    public async Task LOST_retains_payer_debit_and_operation_credit_times_without_relabeling_original_credit_time()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, bankId) = await ReportFixtures.PaidAsync(database);
        var detail = ReportFixtures.From(fixture) with { CreditAt = ReportFixtures.Detail().PaymentAt.AddDays(1) };
        var plain = ReportFixtures.File(ReportFixtures.Profile(), ReportKind.Lost, detail); var id = await ReportFixtures.StageAsync(database, storage, plain, fileName: ReportFixtures.LostName);
        await using var db = database.Open(); var service = ReportFixtures.Service(db, storage, plain); var rowId = await db.ReportRows.Select(x => x.Id).SingleAsync();
        await service.ReconcileAsync(rowId, default);
        Assert.Null((await db.BankTransactions.SingleAsync(x => x.Id == bankId)).PaymentOccurredAt);
        var row = await db.ReportRows.SingleAsync(); var saved = JsonSerializer.Deserialize<ReportDetail>(row.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(detail.PaymentAt, saved!.PaymentAt); Assert.Equal(detail.CreditAt, saved.CreditAt); Assert.Equal("MatchedCredit", row.Outcome);
        await new ReportComparisonHandler(db, PaymentFixture.Commerce, PaymentFixture.Bank, TimeProvider.System).CompareAsync(id, default);
        Assert.Contains("LostRepairRowsOnly", (await db.Reports.AsNoTracking().SingleAsync()).ComparisonJson); Assert.Equal(0, await db.Cases.CountAsync());
    }

    [PgFact]
    public async Task Daily_comparison_finds_exact_missing_ledger_instructions_without_inventing_original_bank_times()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (first, bankId) = await ReportFixtures.PaidAsync(database);
        var (_, secondId) = await ReportFixtures.PaidAsync(database); var (_, unknownTimeId) = await ReportFixtures.PaidAsync(database);
        await using (var db = database.Open()) await db.BankTransactions.Where(x => x.Id == secondId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PaymentOccurredAt, ReportFixtures.Detail().PaymentAt.ToUniversalTime()));
        var plain = ReportFixtures.File(ReportFixtures.Profile(), ReportKind.Iso, ReportFixtures.From(first)); var reportId = await ReportFixtures.StageAsync(database, storage, plain);
        await using (var db = database.Open()) await ReportFixtures.Service(db, storage, plain).ReconcileAsync(await db.ReportRows.Select(x => x.Id).SingleAsync(), default);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ => { await using var db = database.Open(); await new ReportComparisonHandler(db, PaymentFixture.Commerce, PaymentFixture.Bank, TimeProvider.System).CompareAsync(reportId, default); }));
        await using (var db = database.Open())
        {
            var missing = Assert.Single(await db.Cases.Where(x => x.Kind == "LocalPaymentAbsentFromReport").ToArrayAsync()); Assert.Equal(secondId, missing.BankTransactionId);
            Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.Action == "report.compare")); Assert.Equal(3, await db.Allocations.CountAsync());
            var report = await db.Reports.SingleAsync(); using var summary = JsonDocument.Parse(report.ComparisonJson);
            Assert.Equal(2, summary.RootElement.GetProperty("knownTimestampPayments").GetInt32()); Assert.Equal(1, summary.RootElement.GetProperty("absentCount").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("unknownOriginalTimeAcrossLedger").GetInt32()); Assert.False(summary.RootElement.GetProperty("timeAttributionComplete").GetBoolean());
            Assert.Null((await db.BankTransactions.SingleAsync(x => x.Id == unknownTimeId)).PaymentOccurredAt); Assert.NotNull((await db.BankTransactions.SingleAsync(x => x.Id == bankId)).PaymentOccurredAt);
        }
    }

    private sealed class FakeSource : IReportSource
    {
        public int Calls; public byte[]? Content;
        public Task<byte[]?> DownloadAsync(string name, CancellationToken cancellationToken) { Interlocked.Increment(ref Calls); return Task.FromResult(Content); }
    }

    [PgFact]
    public async Task Missing_file_is_waiting_then_overdue_once_and_later_download_is_never_treated_as_zero_settlement()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var options = ReportFixtures.Profile();
        options.Enabled = true; options.Username = "synthetic"; options.Password = "synthetic-only"; options.HostKeySha256 = "SHA256:" + new string('A', 43);
        options.PgpPrivateKeyPath = "synthetic-only"; options.RecipientFingerprint = new string('A', 40);
        var source = new FakeSource(); var clock = new MutableClock(DateTimeOffset.UtcNow); var due = clock.GetUtcNow().AddHours(1);
        var plain = ReportFixtures.File(options, ReportKind.Iso, ReportFixtures.Detail());
        async Task CheckAsync() { await using var db = database.Open(); var reports = ReportFixtures.Service(db, storage, plain, options, clock); await new ReportFetchService(db, PaymentFixture.Commerce, PaymentFixture.Bank, options, source, reports, clock).CheckAsync(ReportFixtures.IsoName, due, default); }
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CheckAsync())); Assert.Equal(1, source.Calls);
        await using (var db = database.Open()) { Assert.Equal("WaitingForFile", (await db.ReportFetches.SingleAsync()).State); Assert.Equal(0, await db.Cases.CountAsync()); Assert.Equal(0, await db.Reports.CountAsync()); }
        clock.Advance(TimeSpan.FromHours(2)); await CheckAsync(); clock.Advance(TimeSpan.FromMinutes(31)); await CheckAsync();
        await using (var db = database.Open()) Assert.Equal(1, await db.Cases.CountAsync(x => x.Kind == "ReportOverdue"));
        source.Content = RandomNumberGenerator.GetBytes(100); clock.Advance(TimeSpan.FromMinutes(31)); await CheckAsync();
        await using (var db = database.Open()) { Assert.Equal("Downloaded", (await db.ReportFetches.SingleAsync()).State); Assert.Equal(1, await db.Reports.CountAsync()); Assert.Equal(0, await db.Allocations.CountAsync()); Assert.Equal("Resolved", (await db.Cases.SingleAsync()).State); }
    }

    [PgFact]
    public async Task Bad_totals_or_beneficiary_quarantine_the_complete_encrypted_archive_without_staging_rows()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage();
        var invalidBeneficiary = ReportFixtures.File(ReportFixtures.Profile(), ReportKind.Iso, ReportFixtures.Detail() with { MerchantAccount = "9990000000" });
        var first = await ReportFixtures.StageAsync(database, storage, invalidBeneficiary);
        var invalidTotal = ReportFixtures.File(ReportFixtures.Profile(), ReportKind.Iso, ReportFixtures.Detail()); invalidTotal[2 * 502 + 42] = (byte)'4';
        var second = await ReportFixtures.StageAsync(database, storage, invalidTotal);
        await using var db = database.Open(); Assert.Equal(0, await db.ReportRows.CountAsync()); Assert.Equal(0, await db.Allocations.CountAsync());
        Assert.Equal("report_beneficiary", (await db.Reports.SingleAsync(x => x.Id == first)).FailureCode);
        Assert.Equal("report_totals", (await db.Reports.SingleAsync(x => x.Id == second)).FailureCode);
        Assert.All(await db.Reports.ToArrayAsync(), x => Assert.Equal("Quarantined", x.State)); Assert.Equal(2, await db.Cases.CountAsync());
    }

    [PgFact]
    public async Task Concurrent_import_stage_and_replay_keep_one_immutable_row_and_one_payment_effect()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, bankId) = await ReportFixtures.PaidAsync(database);
        var plain = ReportFixtures.File(ReportFixtures.Profile(), ReportKind.Iso, ReportFixtures.From(fixture)); var cipher = RandomNumberGenerator.GetBytes(100);
        var ids = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); return (await ReportFixtures.Service(db, storage, plain).ImportAsync(ReportFixtures.IsoName, cipher, default)).Id; }));
        Assert.Single(ids.Distinct());
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => { await using var db = database.Open(); await ReportFixtures.Service(db, storage, plain).StageAsync(ids[0], false, default); }));
        Guid rowId;
        await using (var db = database.Open()) rowId = await db.ReportRows.Select(x => x.Id).SingleAsync();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Open(); await ReportFixtures.Service(db, storage, plain).ReconcileAsync(rowId, default); }));
        await using (var db = database.Open())
        {
            Assert.Equal("Complete", (await db.Reports.SingleAsync()).State); Assert.Equal("MatchedCredit", (await db.ReportRows.SingleAsync()).Outcome);
            Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Equal(1, await db.StockMovements.CountAsync(x => x.EffectKey.StartsWith("commit:")));
            var bank = await db.BankTransactions.SingleAsync(x => x.Id == bankId); Assert.Equal(ReportFixtures.Detail().PaymentAt, bank.PaymentOccurredAt); Assert.Equal("ReportIsoPayment", bank.PaymentTimeSource);
        }
        var replayId = await ReportFixtures.StageAsync(database, storage, plain); // new PGP ciphertext, identical plaintext
        await using (var db = database.Open())
        {
            Assert.Equal("Replay", (await db.Reports.SingleAsync(x => x.Id == replayId)).State); Assert.Equal(ids[0], (await db.Reports.SingleAsync(x => x.Id == replayId)).ReplayOfId);
            Assert.Equal(1, await db.ReportRows.CountAsync()); Assert.Equal(cipher, await File.ReadAllBytesAsync(storage.Files.PathFor("reports", BankWire.Hash(cipher), ".pgp")));
        }
    }

    [PgFact]
    public async Task Changed_content_requires_audited_revision_approval_and_corrupt_archive_never_stages()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var profile = ReportFixtures.Profile();
        var first = ReportFixtures.File(profile, ReportKind.Iso, ReportFixtures.Detail()); await ReportFixtures.StageAsync(database, storage, first);
        var changed = ReportFixtures.File(profile, ReportKind.Iso, ReportFixtures.Detail() with { FeeMinor = 26 }); var id = await ReportFixtures.StageAsync(database, storage, changed);
        await using (var db = database.Open())
        {
            Assert.Equal("RevisionReview", (await db.Reports.SingleAsync(x => x.Id == id)).State); Assert.Equal(1, await db.ReportRows.CountAsync());
            var service = ReportFixtures.Service(db, storage, changed);
            await service.ReprocessAsync(id, "finance:test", "Validated bank reissue", default); await service.StageAsync(id, true, default);
            Assert.Equal(2, await db.ReportRows.CountAsync()); Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.Action == "report.reprocess"));
        }
        await using (var db = database.Open())
        {
            var service = ReportFixtures.Service(db, storage, first); var archived = await service.ImportAsync(ReportFixtures.IsoName, RandomNumberGenerator.GetBytes(100), default);
            await File.WriteAllTextAsync(storage.Files.PathFor("reports", archived.FileKey, ".pgp"), "corrupt");
            await service.StageAsync(archived.Id, false, default);
            Assert.Equal("report_archive_corrupt", (await db.Reports.AsNoTracking().SingleAsync(x => x.Id == archived.Id)).FailureCode);
            Assert.Equal(2, await db.ReportRows.CountAsync());
        }
    }

    [PgFact]
    public async Task V_and_D_void_one_original_credit_and_hold_delivery_without_restock_or_erasing_receipt()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, bankId) = await ReportFixtures.PaidAsync(database);
        Guid receiptId;
        await using (var db = database.Open())
        {
            var receipt = await new ReceiptService(db, new(db, TimeProvider.System), PaymentFixture.Commerce, new(), TimeProvider.System, storage.Files).EnsureAsync(fixture.OrderId, default); receiptId = receipt.Id;
            var line = await db.OrderLines.SingleAsync();
            db.Fulfillments.Add(new FulfillmentItem { OrderId = fixture.OrderId, OrderLineId = line.Id, Kind = ProductKind.Physical, Quantity = 1, DeliveryMethod = "Pickup" });
            db.Entitlements.Add(new Entitlement { ShopperId = fixture.ShopperId, OrderId = fixture.OrderId, OrderLineId = line.Id, VariantId = fixture.VariantId, ProductId = (await db.Variants.SingleAsync(x => x.Id == fixture.VariantId)).ProductId, DigitalKind = DigitalDeliveryKind.InventoryItem });
            await db.SaveChangesAsync();
        }
        var original = ReportFixtures.From(fixture, "V"); var debit = ReportFixtures.From(fixture, "D", "VOID" + Guid.NewGuid().ToString("N")[..31], fixture.Notice.InstructionId!);
        var plain = ReportFixtures.File(ReportFixtures.Profile(), ReportKind.Iso, original, debit); var id = await ReportFixtures.StageAsync(database, storage, plain);
        Guid[] rows;
        await using (var db = database.Open()) rows = await db.ReportRows.Where(x => x.ReportArchiveId == id).Select(x => x.Id).ToArrayAsync();
        await Task.WhenAll(rows.Select(async row => { await using var db = database.Open(); await ReportFixtures.Service(db, storage, plain).ReconcileAsync(row, default); }));
        await using (var db = database.Open())
        {
            Assert.True((await db.BankTransactions.SingleAsync(x => x.Id == bankId)).Voided); Assert.Equal(PaymentState.Voided, (await db.Orders.SingleAsync()).PaymentState);
            Assert.Equal(OrderState.Review, (await db.Orders.SingleAsync()).State); Assert.Equal(EntitlementState.Revoked, (await db.Entitlements.SingleAsync()).State);
            Assert.Equal(FulfillmentState.Review, (await db.Fulfillments.SingleAsync()).State); Assert.Equal(4, (await db.Variants.SingleAsync()).OnHand);
            Assert.Equal(receiptId, (await db.Documents.SingleAsync()).Id); Assert.Equal(1, await db.Cases.CountAsync(x => x.Kind == "ReportedVoidFulfillmentReview"));
            Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Equal(1, await db.StockMovements.CountAsync(x => x.EffectKey.StartsWith("commit:")));
        }
    }

    [PgFact]
    public async Task Fifty_character_API_instruction_is_never_prefix_allocated_from_a_thirty_five_column_report()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var (fixture, _) = await ReportFixtures.PaidAsync(database);
        await using (var db = database.Open()) await db.BankTransactions.ExecuteUpdateAsync(s => s.SetProperty(x => x.InstructionId, fixture.Notice.InstructionId + new string('X', 15)));
        var plain = ReportFixtures.File(ReportFixtures.Profile(), ReportKind.Iso, ReportFixtures.From(fixture)); await ReportFixtures.StageAsync(database, storage, plain);
        await using (var db = database.Open())
        {
            await ReportFixtures.Service(db, storage, plain).ReconcileAsync(await db.ReportRows.Select(x => x.Id).SingleAsync(), default);
            Assert.Equal("ReportInstructionWidthMismatch", (await db.ReportRows.SingleAsync()).Outcome); Assert.Equal(OrderState.Review, (await db.Orders.SingleAsync()).State);
            Assert.Equal(1, await db.BankTransactions.CountAsync()); Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Null((await db.BankTransactions.SingleAsync()).PaymentOccurredAt);
        }
    }

    [PgFact]
    public async Task Report_finance_endpoints_are_private_role_checked_and_never_expose_payer_rows()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage(); var plain = ReportFixtures.File(ReportFixtures.Profile(), ReportKind.Iso, ReportFixtures.Detail());
        var id = await ReportFixtures.StageAsync(database, storage, plain);
        await using var factory = new CommerceApiFactory(database.ConnectionString);
        using var buyer = factory.ShopperClient("buyer"); using var admin = factory.ShopperClient("admin"); await CartTests.SignInAsync(buyer); await CartTests.SignInAsync(admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync("/ttb-commerce/v1/admin/reports")).StatusCode);
        var response = await admin.GetAsync($"/ttb-commerce/v1/admin/reports/{id}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("9999999999", text); Assert.DoesNotContain("นาย ทดสอบ", text);
        admin.DefaultRequestHeaders.Remove("X-CSRF-Token");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/ttb-commerce/v1/admin/reports/{id}/reprocess", new { reason = "test" })).StatusCode);
    }
}

public sealed class PgpReportTests
{
    [Fact]
    public void Actual_PGP_requires_pinned_recipient_AES256_integrity_and_bounded_decompression()
    {
        using var storage = new TestPrivateStorage(); Directory.CreateDirectory(storage.Root); var random = new SecureRandom();
        var generator = new RsaKeyPairGenerator(); generator.Init(new KeyGenerationParameters(random, 2048));
        var pair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaGeneral, generator.GenerateKeyPair(), DateTime.UtcNow);
        var secret = new PgpSecretKey(PgpSignature.DefaultCertification, pair, "Synthetic report fixture", SymmetricKeyAlgorithmTag.Aes256, "test-only".ToCharArray(), true, null, null, random);
        var keyPath = Path.Combine(storage.Root, "fixture-key.pgp"); File.WriteAllBytes(keyPath, secret.GetEncoded());
        byte[] Encrypt(byte[] plain, SymmetricKeyAlgorithmTag cipher = SymmetricKeyAlgorithmTag.Aes256, bool integrity = true, bool compress = false, bool sign = false)
        {
            using var literalBuffer = new MemoryStream(); var literal = new PgpLiteralDataGenerator();
            PgpSignatureGenerator? signature = null;
            if (sign)
            {
                signature = new(secret.PublicKey.Algorithm, HashAlgorithmTag.Sha256); signature.InitSign(PgpSignature.BinaryDocument, secret.ExtractPrivateKey("test-only".ToCharArray()));
                signature.GenerateOnePassVersion(false).Encode(literalBuffer);
            }
            using (var stream = literal.Open(literalBuffer, PgpLiteralData.Binary, "../../untrusted-name.TXT", plain.Length, DateTime.UtcNow)) stream.Write(plain);
            if (signature is not null) { signature.Update(plain); signature.Generate().Encode(literalBuffer); }
            var packets = literalBuffer.ToArray();
            if (compress) { using var zipped = new MemoryStream(); var compressor = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip); using (var stream = compressor.Open(zipped)) stream.Write(packets); packets = zipped.ToArray(); }
            using var output = new MemoryStream(); var encryptor = new PgpEncryptedDataGenerator(cipher, integrity, random); encryptor.AddMethod(secret.PublicKey);
            using (var stream = encryptor.Open(output, packets.Length)) stream.Write(packets); return output.ToArray();
        }
        var options = ReportFixtures.Profile(); options.PgpPrivateKeyPath = keyPath; options.PgpPassphrase = "test-only"; options.RecipientFingerprint = Convert.ToHexString(secret.PublicKey.GetFingerprint());
        var decryptor = new PgpReportDecryptor(options); var plain = ReportFixtures.File(options, ReportKind.Iso, ReportFixtures.Detail()); var encrypted = Encrypt(plain, compress: true);
        Assert.Equal(plain, decryptor.Decrypt(encrypted));
        Assert.Throws<CommerceException>(() => decryptor.Decrypt(plain));
        Assert.Throws<CommerceException>(() => decryptor.Decrypt(Encrypt(plain, SymmetricKeyAlgorithmTag.Aes128)));
        Assert.Throws<CommerceException>(() => decryptor.Decrypt(Encrypt(plain, integrity: false)));
        var damaged = encrypted.ToArray(); damaged[^5] ^= 1; Assert.Throws<CommerceException>(() => decryptor.Decrypt(damaged));
        options.SigningFingerprint = new string('A', 40); Assert.Equal("report_pgp_signature_required", Assert.Throws<CommerceException>(() => decryptor.Decrypt(encrypted)).Code);
        options.SigningPublicKeyPath = Path.Combine(storage.Root, "fixture-public.pgp"); File.WriteAllBytes(options.SigningPublicKeyPath, secret.PublicKey.GetEncoded());
        options.SigningFingerprint = options.RecipientFingerprint; var signed = Encrypt(plain, compress: true, sign: true); Assert.Equal(plain, decryptor.Decrypt(signed));
        options.SigningFingerprint = new string('A', 40); Assert.Equal("report_pgp_signer", Assert.Throws<CommerceException>(() => decryptor.Decrypt(signed)).Code); options.SigningFingerprint = "";
        options.MaxFileBytes = 1024; Assert.Equal("report_size", Assert.Throws<CommerceException>(() => decryptor.Decrypt(Encrypt(new byte[10000], compress: true))).Code);
        options.MaxFileBytes = 33554432; options.RecipientFingerprint = new string('A', 40); Assert.Throws<CommerceException>(() => decryptor.Decrypt(encrypted));
    }
}
