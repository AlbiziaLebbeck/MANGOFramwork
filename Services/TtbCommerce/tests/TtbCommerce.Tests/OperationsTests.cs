using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TtbCommerce.Api;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class OperationsTests
{
    private const string Token = "SYNTHETIC-METRICS-TOKEN-ONLY-FOR-TESTS";
    private sealed class Factory(string connection, bool enabled, string token, bool frozen = false) : CommerceApiFactory(connection)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Operations:MetricsEnabled"] = enabled.ToString(), ["Operations:MetricsToken"] = token, ["Operations:RecoveryFreeze"] = frozen.ToString(), ["Commerce:Role"] = frozen ? "All" : "Api" }));
        }
    }
    [Fact]
    public void Storage_capacity_reads_the_existing_filesystem_without_creating_directories_or_exporting_file_metadata()
    {
        using var storage = new TestPrivateStorage();
        Assert.False(Directory.Exists(storage.Root));
        Assert.False(PrivateStorageCapacityProbe.Read(storage.Files).Available);
        Assert.False(Directory.Exists(storage.Root));
        Directory.CreateDirectory(storage.Root);
        var capacity = PrivateStorageCapacityProbe.Read(storage.Files);
        Assert.True(capacity.Available); Assert.True(capacity.TotalBytes > 0);
        Assert.InRange(capacity.AvailableBytes, 0, capacity.TotalBytes);
        Assert.Empty(Directory.EnumerateFileSystemEntries(storage.Root));
    }

    [PgFact]
    public async Task Metrics_preserve_private_capacity_and_callback_telemetry_when_only_the_disposable_test_database_disappears()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage();
        Directory.CreateDirectory(storage.Root);
        await using var factory = new Factory(database.ConnectionString, true, Token) { PrivateRoot = storage.Root };
        using var client = factory.ShopperClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        var before = await client.GetStringAsync("/internal/metrics"); Assert.Contains("ttb_database_up 1\n", before);
        factory.Services.GetRequiredService<CallbackTelemetry>().Observe(.1, 500, false);
        // Dispose is guarded by PgDatabase's random ttb_test_ identity; it never targets UAT or the shared PG service.
        await database.DisposeAsync();
        var metrics = await client.GetStringAsync("/internal/metrics");
        Assert.Contains("ttb_database_up 0\n", metrics); Assert.Contains("ttb_private_storage_capacity_probe_success 1\n", metrics);
        Assert.Contains("ttb_private_storage_filesystem_available_bytes ", metrics); Assert.Contains("ttb_recovery_frozen 0\n", metrics);
        Assert.Contains("ttb_callback_http_duration_seconds_count{outcome=\"server_error\"} 1", metrics);
        Assert.Contains("ttb_bank_circuit_open{service=\"merchant-void\"} 0\n", metrics);
        Assert.DoesNotContain("ttb_jobs_pending", metrics); Assert.DoesNotContain(storage.Root, metrics); Assert.DoesNotContain(Token, metrics);
    }

    [PgFact]
    public async Task A_missing_private_mount_is_an_explicit_probe_failure_instead_of_a_fabricated_zero_capacity()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage();
        await using var factory = new Factory(database.ConnectionString, true, Token) { PrivateRoot = storage.Root };
        using var client = factory.ShopperClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        var metrics = await client.GetStringAsync("/internal/metrics");
        Assert.Contains("ttb_private_storage_capacity_probe_success 0\n", metrics);
        Assert.DoesNotContain("ttb_private_storage_filesystem_size_bytes", metrics); Assert.DoesNotContain("ttb_private_storage_filesystem_available_bytes", metrics);
        Assert.False(Directory.Exists(storage.Root)); Assert.DoesNotContain(storage.Root, metrics);
    }
    [PgFact]
    public async Task Recovery_freeze_keeps_reads_available_blocks_mutations_and_stops_every_worker_before_claiming_a_job()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using (var db = database.Open())
        {
            foreach (var kind in new[] { "order.expire", "document.render", "report.stage" })
                db.Jobs.Add(new OutboxJob { EffectKey = "frozen:" + kind, Kind = kind, AvailableAt = DateTimeOffset.UtcNow.AddSeconds(-1), PayloadJson = "{}" });
            await db.SaveChangesAsync();
        }
        await using var factory = new Factory(database.ConnectionString, true, Token, frozen: true); using var client = factory.ShopperClient();
        var readiness = JsonDocument.Parse(await client.GetStringAsync("/health/ready")).RootElement;
        Assert.True(readiness.GetProperty("recoveryFrozen").GetBoolean()); Assert.False(readiness.GetProperty("paymentsReady").GetBoolean());
        var config = JsonDocument.Parse(await client.GetStringAsync("/ttb-commerce/v1/storefront/config")).RootElement;
        Assert.False(config.GetProperty("paymentsReady").GetBoolean());
        foreach (var path in new[] { "/ttb-commerce/v1/session/local", "/ttb-commerce/v1/orders", "/ttb/v1/credit-notification", "/ttb-commerce/v1/admin/voids/00000000-0000-0000-0000-000000000001/enquiry" })
        {
            var response = await client.PostAsync(path, new StringContent("{}")); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("recovery_frozen", await response.Content.ReadAsStringAsync()); Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Contains("https://shop.test", response.Headers.GetValues("Access-Control-Allow-Origin"));
        }
        var workers = factory.Services.GetServices<IHostedService>().OfType<BackgroundService>().Where(x => x.GetType().Name is "CommerceWorker" or "ReceiptWorker" or "ReportWorker").ToArray();
        Assert.Equal(3, workers.Length);
        for (var i = 0; i < 100 && workers.Any(x => x.ExecuteTask?.IsCompletedSuccessfully != true); i++) await Task.Delay(10);
        Assert.All(workers, worker => Assert.True(worker.ExecuteTask?.IsCompletedSuccessfully));
        await using var check = database.Open(); Assert.Equal(3, await check.Jobs.CountAsync()); Assert.False(await check.Jobs.AnyAsync(x => x.LeaseToken != null || x.CompletedAt != null || x.Attempts > 0));
        Assert.Equal(0, await check.CallbackReceipts.CountAsync()); Assert.Equal(0, await check.VoidRequests.CountAsync());
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token); Assert.Contains("ttb_recovery_frozen 1\n", await client.GetStringAsync("/internal/metrics"));
    }
    [PgFact]
    public async Task Metrics_are_private_disabled_by_default_and_require_a_separate_collector_token_even_for_finance()
    {
        await using var database = await PgDatabase.CreateAsync();
        foreach (var (enabled, token) in new[] { (false, Token), (true, "short"), (true, new string('x', 513)) })
        {
            await using var factory = new Factory(database.ConnectionString, enabled, token); using var client = factory.ShopperClient("admin");
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/internal/metrics")).StatusCode);
        }
        await using var secured = new Factory(database.ConnectionString, true, Token); using var collector = secured.ShopperClient("admin");
        await CartTests.SignInAsync(collector); Assert.Equal(HttpStatusCode.Unauthorized, (await collector.GetAsync("/internal/metrics")).StatusCode);
        collector.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-collector-token"); Assert.Equal(HttpStatusCode.Unauthorized, (await collector.GetAsync("/internal/metrics")).StatusCode);
        collector.DefaultRequestHeaders.Authorization = new("Bearer", Token); var result = await collector.GetAsync("/internal/metrics"); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.True(result.Headers.CacheControl?.NoStore); Assert.Equal("text/plain", result.Content.Headers.ContentType?.MediaType);
    }
    [PgFact]
    public async Task Metrics_count_only_due_work_and_missing_reports_and_never_export_financial_identifiers_or_payloads()
    {
        await using var database = await PgDatabase.CreateAsync(); var now = DateTimeOffset.UtcNow; const string secret = "PRIVATE-PAYER-AND-INSTRUCTION-DO-NOT-EXPORT";
        await using (var db = database.Open())
        {
            db.Jobs.AddRange(
                new OutboxJob { EffectKey = "metric-retry", Kind = "receipt.render", CreatedAt = now.AddHours(-1), AvailableAt = now.AddSeconds(-20), Attempts = 2, LastError = secret, PayloadJson = "{\"private\":\"" + secret + "\"}" },
                new OutboxJob { EffectKey = "metric-future", Kind = "order.expire", CreatedAt = now.AddHours(-1), AvailableAt = now.AddMinutes(10) },
                new OutboxJob { EffectKey = "metric-complete", Kind = "receipt.render", CompletedAt = now });
            db.Cases.Add(new ReconciliationCase { EffectKey = "metric-case", Kind = secret, EvidenceJson = "{\"private\":\"" + secret + "\"}" });
            db.ReportFetches.AddRange(new ReportFetch { MerchantId = "SIM", FileName = "PRIVATE-FILE-NAME-1", State = "Downloaded", DueAt = now.AddHours(-1) },
                new ReportFetch { MerchantId = "SIM", FileName = "PRIVATE-FILE-NAME-2", State = "WaitingForFile", DueAt = now.AddMinutes(-5) });
            await db.SaveChangesAsync();
        }
        await using var factory = new Factory(database.ConnectionString, true, Token); using var client = factory.ShopperClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        var metrics = await client.GetStringAsync("/internal/metrics");
        Assert.Contains("ttb_jobs_pending 2\n", metrics); Assert.Contains("ttb_jobs_retrying 1\n", metrics); Assert.Contains("ttb_reports_overdue 1\n", metrics); Assert.Contains("ttb_finance_open_cases 1\n", metrics);
        var age = double.Parse(metrics.Split('\n').Single(x => x.StartsWith("ttb_jobs_oldest_due_age_seconds ", StringComparison.Ordinal)).Split(' ')[1], CultureInfo.InvariantCulture);
        Assert.InRange(age, 20, 300); Assert.DoesNotContain(secret, metrics); Assert.DoesNotContain("PRIVATE-FILE", metrics); Assert.DoesNotContain(Token, metrics);
        Assert.Equal(4, metrics.Split('\n').Count(line => line.StartsWith("ttb_bank_circuit_open{service=", StringComparison.Ordinal)));
    }
    [Fact]
    public async Task Callback_histograms_have_bounded_labels_cumulative_buckets_and_thread_safe_committed_ACK_counts()
    {
        var telemetry = new CallbackTelemetry();
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => { telemetry.Observe(.2, 200, false); telemetry.CommittedAck("0000"); })));
        telemetry.Observe(.9, 403, true); telemetry.CommittedAck("PRIVATE-UNTRUSTED-CODE"); var metrics = telemetry.Render();
        Assert.Contains("outcome=\"accepted\",le=\"0.1\"} 0", metrics); Assert.Contains("outcome=\"accepted\",le=\"0.25\"} 50", metrics);
        Assert.Contains("outcome=\"accepted\",le=\"+Inf\"} 50", metrics); Assert.Contains("ttb_callback_committed_acks_total{ack=\"0000\"} 50", metrics);
        Assert.Contains("ttb_callback_committed_acks_total{ack=\"other\"} 1", metrics); Assert.DoesNotContain("PRIVATE-UNTRUSTED-CODE", metrics);
    }
}
