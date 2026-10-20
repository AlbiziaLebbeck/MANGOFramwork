using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

// Fixed labels only: never export user, order, instruction, filename, payload, reason or credential data.
public sealed class CallbackTelemetry
{
    private readonly object gate = new();
    private static readonly double[] Bounds = [.05, .1, .25, .5, 1, 2, 5, 10];
    private readonly Dictionary<string, (long Count, double Sum, long[] Buckets)> durations = new();
    private readonly Dictionary<string, long> acks = new();
    public void Observe(double seconds, int status, bool exception)
    {
        var outcome = exception ? "exception" : status >= 500 ? "server_error" : status >= 400 ? "rejected" : "accepted";
        lock (gate)
        {
            var value = durations.GetValueOrDefault(outcome, (0, 0, new long[Bounds.Length]));
            value.Count++; value.Sum += seconds;
            for (var i = 0; i < Bounds.Length; i++) if (seconds <= Bounds[i]) value.Buckets[i]++;
            durations[outcome] = value;
        }
    }
    public void CommittedAck(string code)
    {
        var label = code is "0000" or "0001" or "0002" or "0003" or "0004" or "0005" or "0006" or "0007" ? code : "other";
        lock (gate) acks[label] = acks.GetValueOrDefault(label) + 1;
    }
    public string Render()
    {
        var text = new StringBuilder("# TYPE ttb_callback_http_duration_seconds histogram\n# TYPE ttb_callback_committed_acks_total counter\n");
        lock (gate)
        {
            foreach (var (outcome, value) in durations.OrderBy(x => x.Key))
            {
                for (var i = 0; i < Bounds.Length; i++) text.Append(CultureInfo.InvariantCulture, $"ttb_callback_http_duration_seconds_bucket{{outcome=\"{outcome}\",le=\"{Bounds[i]}\"}} {value.Buckets[i]}\n");
                text.Append(CultureInfo.InvariantCulture, $"ttb_callback_http_duration_seconds_bucket{{outcome=\"{outcome}\",le=\"+Inf\"}} {value.Count}\nttb_callback_http_duration_seconds_sum{{outcome=\"{outcome}\"}} {value.Sum}\nttb_callback_http_duration_seconds_count{{outcome=\"{outcome}\"}} {value.Count}\n");
            }
            foreach (var (code, count) in acks.OrderBy(x => x.Key)) text.Append(CultureInfo.InvariantCulture, $"ttb_callback_committed_acks_total{{ack=\"{code}\"}} {count}\n");
        }
        return text.ToString();
    }
}

public static class OperationsEndpoints
{
    public static void UseCallbackTelemetry(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path != "/ttb/v1/credit-notification") { await next(context); return; }
            var started = Stopwatch.GetTimestamp(); var failed = true;
            try { await next(context); failed = false; }
            finally { context.RequestServices.GetRequiredService<CallbackTelemetry>().Observe(Stopwatch.GetElapsedTime(started).TotalSeconds, context.Response.StatusCode, failed); }
        });
    }
    public static void MapOperationsEndpoints(this WebApplication app)
    {
        app.MapGet("/internal/metrics", async (HttpContext http, OperationsOptions options, CommerceDb db, CommerceOptions commerce, BankReadiness bank, TtbOptions bankOptions, ReportOptions reports, TimeProvider clock, CallbackTelemetry telemetry, PrivateFiles files, BankCircuitBreaker circuit) =>
        {
            if (!options.MetricsEnabled || options.MetricsToken.Length is < 32 or > 512) return Results.NotFound();
            var header = http.Request.Headers.Authorization.ToString();
            if (header.Length > 1024 || !header.StartsWith("Bearer ", StringComparison.Ordinal) || !SecretTokens.Matches(header[7..], SecretTokens.Hash(options.MetricsToken))) return Results.Unauthorized();
            var cancellation = http.RequestAborted; var now = clock.GetUtcNow(); var output = new StringBuilder();
            void Gauge(string name, double value) => output.Append(CultureInfo.InvariantCulture, $"# TYPE {name} gauge\n{name} {Math.Max(0, value)}\n");
            Gauge("ttb_recovery_frozen", options.RecoveryFreeze ? 1 : 0);
            Gauge("ttb_bank_profile_ready", bank.Missing().Count == 0 ? 1 : 0);
            var signingKey = BankSigningKeyProbe.Read(commerce, bankOptions, options);
            Gauge("ttb_bank_signing_key_expected", signingKey.Expected ? 1 : 0);
            Gauge("ttb_bank_signing_key_probe_success", signingKey.Usable ? 1 : 0);
            Gauge("ttb_bank_signing_key_rotation_scheduled", signingKey.RotationScheduled ? 1 : 0);
            if (signingKey.RotationDueUnixSeconds is { } rotationDue) Gauge("ttb_bank_signing_key_rotation_due_timestamp_seconds", rotationDue);
            Gauge("ttb_reports_enabled", reports.Enabled ? 1 : 0);
            Gauge("ttb_reports_profile_ready", reports.Enabled && reports.Missing().Count == 0 && File.Exists(reports.PgpPrivateKeyPath) ? 1 : 0);
            output.Append($"# TYPE ttb_build_reader_contract gauge\nttb_build_reader_contract{{environment=\"{commerce.Environment}\",role=\"{commerce.Role}\"}} 3\n");
            output.Append("# TYPE ttb_bank_circuit_open gauge\n# TYPE ttb_bank_circuit_consecutive_failures gauge\n# TYPE ttb_bank_circuit_cooldown_remaining_seconds gauge\n");
            foreach (var state in circuit.Read())
            {
                var service = BankCircuitBreaker.ServiceName(state.Operation);
                output.Append(CultureInfo.InvariantCulture, $"ttb_bank_circuit_open{{service=\"{service}\"}} {(state.Open ? 1 : 0)}\nttb_bank_circuit_consecutive_failures{{service=\"{service}\"}} {state.ConsecutiveFailures}\nttb_bank_circuit_cooldown_remaining_seconds{{service=\"{service}\"}} {state.SecondsRemaining}\n");
            }
            var capacity = PrivateStorageCapacityProbe.Read(files);
            Gauge("ttb_private_storage_capacity_probe_success", capacity.Available ? 1 : 0);
            if (capacity.Available)
            {
                Gauge("ttb_private_storage_filesystem_size_bytes", capacity.TotalBytes);
                Gauge("ttb_private_storage_filesystem_available_bytes", capacity.AvailableBytes);
            }
            // Preserve independent storage/process signals during a database outage. Never emit stale financial gauges.
            var databaseReady = await db.Database.CanConnectAsync(cancellation);
            Gauge("ttb_database_up", databaseReady ? 1 : 0);
            if (!databaseReady) return Results.Text(output.Append(telemetry.Render()).ToString(), "text/plain; version=0.0.4; charset=utf-8");
            var jobs = db.Jobs.AsNoTracking().Where(x => x.CompletedAt == null);
            Gauge("ttb_jobs_pending", await jobs.CountAsync(cancellation));
            Gauge("ttb_jobs_retrying", await jobs.CountAsync(x => x.Attempts > 0 && x.LastError != null, cancellation));
            Gauge("ttb_jobs_expired_leases", await jobs.CountAsync(x => x.LeaseToken != null && x.LeaseUntil <= now, cancellation));
            var oldest = await jobs.Where(x => x.AvailableAt <= now).MinAsync(x => (DateTimeOffset?)x.AvailableAt, cancellation);
            Gauge("ttb_jobs_oldest_due_age_seconds", oldest is null ? 0 : (now - oldest.Value).TotalSeconds);
            Gauge("ttb_receipts_pending", await db.Documents.CountAsync(x => x.State != "Ready", cancellation));
            Gauge("ttb_finance_open_cases", await db.Cases.CountAsync(x => x.State == "Open", cancellation));
            Gauge("ttb_voids_unknown", await db.Voids.CountAsync(x => x.State == "Unknown", cancellation));
            Gauge("ttb_reports_waiting", await db.ReportFetches.CountAsync(x => x.State == "WaitingForFile", cancellation));
            Gauge("ttb_reports_overdue", await db.ReportFetches.CountAsync(x => x.State != "Downloaded" && x.DueAt < now, cancellation));
            Gauge("ttb_reports_quarantined", await db.Reports.CountAsync(x => x.State == "Quarantined", cancellation));
            Gauge("ttb_inventory_invariant_failures", await db.Variants.CountAsync(x => x.TrackStock && (x.OnHand < 0 || x.Reserved < 0 || x.OnHand < x.Reserved), cancellation));
            Gauge("ttb_callback_receipts", await db.CallbackReceipts.CountAsync(cancellation));
            Gauge("ttb_callback_repeated_deliveries", (double)(await db.CallbackReceipts.SumAsync(x => (long)x.DeliveryCount - 1, cancellation)));
            var since = now.AddHours(-1);
            foreach (var service in new[] { "verify-qr", "enquiry-qr-range", "merchant-void", "enquiry-void" })
            {
                var count = service is "verify-qr" or "enquiry-qr-range"
                    ? await db.VerificationRequests.CountAsync(x => x.Service == service && x.CreatedAt >= since, cancellation)
                    : await db.VoidRequests.CountAsync(x => x.Service == service && x.CreatedAt >= since, cancellation);
                output.Append(CultureInfo.InvariantCulture, $"ttb_bank_requests_last_hour{{service=\"{service}\"}} {count}\n");
            }
            output.Append(telemetry.Render()); return Results.Text(output.ToString(), "text/plain; version=0.0.4; charset=utf-8");
        });
    }
}
