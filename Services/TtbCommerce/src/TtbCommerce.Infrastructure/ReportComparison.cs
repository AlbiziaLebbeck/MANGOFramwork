using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class ReportComparisonHandler(CommerceDb db, CommerceOptions commerce, TtbOptions bank, TimeProvider clock) : ICommerceJobHandler
{
    public string Kind => "report.compare";
    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(payloadJson); await CompareAsync(json.RootElement.GetProperty("reportId").GetGuid(), cancellationToken);
    }

    public async Task CompareAsync(Guid reportId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var report = (await db.Reports.FromSqlInterpolated($"SELECT * FROM ttb.\"Reports\" WHERE \"Id\" = {reportId} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        if (report.Environment != commerce.Environment || report.MerchantId != bank.MerchantId || report.State != "Complete" || report.ComparisonJson != "{}") return;
        if (report.Kind == ReportKind.Lost)
        {
            // LOST lists operation repairs, not all credits from that day; absence cannot establish a missing payment.
            report.ComparisonJson = JsonSerializer.Serialize(new { scope = "LostRepairRowsOnly", exhaustiveDailyStatement = false });
        }
        else
        {
            using var profile = JsonDocument.Parse(report.AppliedProfileJson);
            var scopeElement = profile.RootElement.GetProperty("dailyScope");
            var scope = scopeElement.ValueKind == JsonValueKind.String ? Enum.Parse<ReportDailyScope>(scopeElement.GetString()!, true) : (ReportDailyScope)scopeElement.GetInt32();
            if (scope == ReportDailyScope.Unknown || !Enum.IsDefined(scope)) throw ReportParser.Error("daily_scope");
            var startLocal = report.FileDate.ToDateTime(TimeOnly.MinValue);
            if (scope == ReportDailyScope.BankingDay2256) startLocal = report.FileDate.AddDays(-1).ToDateTime(new TimeOnly(22, 56));
            var start = new DateTimeOffset(startLocal, TimeSpan.FromHours(7)).ToUniversalTime(); var end = start.AddDays(1);
            var instructions = (await db.Database.SqlQuery<string>($"""
                SELECT r."PayloadJson"->>'instructionId' AS "Value" FROM ttb."ReportRows" r
                WHERE r."ReportArchiveId" = {reportId} AND r."PayloadJson"->>'transactionKind' IN ('C','V')
                """).ToArrayAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var known = await db.BankTransactions.AsNoTracking().Where(x => x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId && x.BillerNo == bank.BillerNo &&
                x.PaymentOccurredAt >= start && x.PaymentOccurredAt < end).Select(x => new { x.Id, x.InstructionId, x.AmountMinor }).ToArrayAsync(cancellationToken);
            var missing = known.Where(x => !instructions.Contains(x.InstructionId)).ToArray();
            foreach (var payment in missing)
            {
                var key = $"report-absent:{reportId}:{payment.Id}";
                if (!await db.Cases.AnyAsync(x => x.EffectKey == key, cancellationToken)) db.Cases.Add(new ReconciliationCase
                {
                    EffectKey = key, Kind = "LocalPaymentAbsentFromReport", BankTransactionId = payment.Id,
                    OrderId = await db.Allocations.Where(x => x.BankTransactionId == payment.Id && x.Primary).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(cancellationToken),
                    EvidenceJson = JsonSerializer.Serialize(new { reportId, scope, start, end, payment.AmountMinor }), CreatedAt = clock.GetUtcNow()
                });
            }
            var unknownTime = await db.BankTransactions.CountAsync(x => x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId && x.BillerNo == bank.BillerNo && x.PaymentOccurredAt == null, cancellationToken);
            var totals = JsonSerializer.Deserialize<ReportTotals>(report.TotalsJson, CatalogService.Json) ?? throw ReportParser.Error("totals_corrupt");
            report.ComparisonJson = JsonSerializer.Serialize(new { scope = scope.ToString(), start, end, knownTimestampPayments = known.Length, absentCount = missing.Length,
                knownOriginalGrossMinor = known.Sum(x => x.AmountMinor), reportCreditMinor = totals.CreditMinor, reportDebitMinor = totals.DebitMinor,
                unknownOriginalTimeAcrossLedger = unknownTime, timeAttributionComplete = unknownTime == 0, warning = "System callback/Verify time is not original payment time. Unknown times cannot prove a daily match." });
        }
        db.AuditEvents.Add(new AuditEvent { Actor = "report-worker", Action = "report.compare", Resource = $"report:{reportId}", DataJson = report.ComparisonJson });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }
}
