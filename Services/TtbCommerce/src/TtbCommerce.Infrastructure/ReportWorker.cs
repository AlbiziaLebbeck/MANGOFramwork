using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class ReportFetchService(CommerceDb db, CommerceOptions commerce, TtbOptions bank, ReportOptions options, IReportSource source, ReportService reports, TimeProvider clock)
{
    public async Task CheckAsync(string fileName, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        ReportParser.FileName(fileName);
        if (!options.Enabled || options.Missing().Count > 0 || commerce.Environment == CommerceEnvironment.Local || commerce.PaymentMode != PaymentMode.Bank) throw ReportParser.Error("transport_unavailable");
        db.ChangeTracker.Clear(); Guid fetchId; var token = Guid.CreateVersion7(); var now = clock.GetUtcNow();
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var key = $"report-fetch:{commerce.Environment}:{bank.MerchantId}:{fileName}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
            var fetch = await db.ReportFetches.SingleOrDefaultAsync(x => x.Environment == commerce.Environment && x.MerchantId == bank.MerchantId && x.FileName == fileName, cancellationToken);
            if (fetch is null)
            {
                fetch = new ReportFetch { Environment = commerce.Environment, MerchantId = bank.MerchantId, FileName = fileName, DueAt = dueAt.ToUniversalTime(), NextCheckAt = now };
                db.ReportFetches.Add(fetch);
            }
            if (fetch.NextCheckAt > now || fetch.LeaseUntil > now) return;
            fetch.LeaseToken = token; fetch.LeaseUntil = now.AddMinutes(3); fetchId = fetch.Id;
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        }
        string state; string? error = null; Guid? reportId = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(90));
            var encrypted = await source.DownloadAsync(fileName, deadline.Token);
            if (encrypted is null) state = "WaitingForFile";
            else { var report = await reports.ImportAsync(fileName, encrypted, deadline.Token); reportId = report.Id; state = "Downloaded"; }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            state = "Failed"; error = exception is CommerceException failure ? failure.Code : exception is OperationCanceledException ? "report_fetch_timeout" : "report_transport_failed";
        }
        db.ChangeTracker.Clear(); await using var resultTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var current = (await db.ReportFetches.FromSqlInterpolated($"SELECT * FROM ttb.\"ReportFetches\" WHERE \"Id\" = {fetchId} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        if (current.LeaseToken != token || current.LeaseUntil <= clock.GetUtcNow()) return;
        current.State = state; current.FailureCode = error; current.ReportArchiveId = reportId ?? current.ReportArchiveId;
        current.LastCheckedAt = clock.GetUtcNow(); current.NextCheckAt = clock.GetUtcNow().AddMinutes(options.PollMinutes); current.LeaseToken = null; current.LeaseUntil = null;
        if (state != "Downloaded" && clock.GetUtcNow() >= current.DueAt)
        {
            var caseKey = $"report-missing:{fetchId}";
            if (!await db.Cases.AnyAsync(x => x.EffectKey == caseKey, cancellationToken)) db.Cases.Add(new ReconciliationCase
            { EffectKey = caseKey, Kind = "ReportOverdue", EvidenceJson = JsonSerializer.Serialize(new { fetchId, fileName, current.DueAt, error }) });
        }
        else if (state == "Downloaded")
        {
            var overdue = await db.Cases.SingleOrDefaultAsync(x => x.EffectKey == $"report-missing:{fetchId}" && x.State == "Open", cancellationToken);
            if (overdue is not null)
            {
                overdue.State = "Resolved"; overdue.Version++; overdue.ResolvedAt = clock.GetUtcNow(); overdue.ResolutionJson = JsonSerializer.Serialize(new { actor = "report-worker", reason = "FileDownloaded", reportId });
                db.AuditEvents.Add(new AuditEvent { Actor = "report-worker", Action = "report.overdue-resolved", Resource = $"case:{overdue.Id}", DataJson = JsonSerializer.Serialize(new { reportId }) });
            }
        }
        await db.SaveChangesAsync(cancellationToken); await resultTransaction.CommitAsync(cancellationToken);
    }
}

public sealed class ReportWorker(IServiceScopeFactory scopes, CommerceOptions commerce, TtbOptions bank, ReportOptions options, TimeProvider clock, ILogger<ReportWorker> logger, OperationsOptions? operations = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (operations?.RecoveryFreeze == true || !options.Enabled || commerce.Role is not (RuntimeRole.All or RuntimeRole.CommerceWorker)) return;
        if (commerce.Environment == CommerceEnvironment.Local || options.Missing().Count > 0 || bank.MerchantId.Length != 3 || bank.BillerNo.Length != 15)
        { logger.LogWarning("Report transport is disabled because its approved provisioning profile is incomplete"); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Numbering.Bangkok).DateTime);
                // Check from D+1 and keep bounded lookback checks for revisions. Missing files never imply zero settlement.
                for (var days = 1; days <= options.LookbackDays; days++)
                foreach (var kind in new[] { ReportKind.Iso, ReportKind.Lost })
                {
                    var date = today.AddDays(-days); var prefix = kind == ReportKind.Iso ? "BPQR_ISO_" : "BPQRLOST_";
                    var name = prefix + date.ToString("yyMMdd", CultureInfo.InvariantCulture) + ".TXT";
                    var due = new DateTimeOffset(date.AddDays(1).ToDateTime(new TimeOnly(kind == ReportKind.Iso ? options.IsoDueHour : options.LostDueHour, 0)), TimeSpan.FromHours(7));
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ReportFetchService>().CheckAsync(name, due, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning("Report worker unavailable: {SafeType}", exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
        }
    }
}
