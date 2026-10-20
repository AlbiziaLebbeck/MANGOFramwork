using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public static class DbGuard
{
    public static async Task AssertExistingEnvironmentAsync(CommerceDb db, CommerceEnvironment environment, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT to_regclass('ttb.\"Deployments\"') IS NOT NULL";
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                command.CommandText = "SELECT \"Environment\" FROM ttb.\"Deployments\" WHERE \"Key\" = 'singleton'";
                var existing = await command.ExecuteScalarAsync(cancellationToken);
                if (existing is string value && value != environment.ToString())
                    throw new InvalidOperationException("The database belongs to a different commerce environment; migration is prohibited.");
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public static async Task AssertEnvironmentAsync(CommerceDb db, CommerceEnvironment environment, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize initialization across all API/worker replicas; never silently adopt an existing different environment.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(817032431)", cancellationToken);
        var identity = await db.Deployments.SingleOrDefaultAsync(x => x.Key == "singleton", cancellationToken);
        if (identity is null)
        {
            db.Deployments.Add(new DeploymentIdentity { Environment = environment });
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (identity.Environment != environment)
            throw new InvalidOperationException("The database belongs to a different commerce environment.");
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed record JobLease(Guid Id, Guid Token, string Kind, string PayloadJson, int Attempts);

public sealed class DurableWork(CommerceDb db, TimeProvider clock)
{
    public static OutboxJob Enqueue(string effectKey, string kind, string payloadJson) => new()
    {
        EffectKey = effectKey, Kind = kind, PayloadJson = payloadJson
    };

    public async Task<JobLease?> ClaimAsync(string[] kinds, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        if (kinds.Length == 0 || leaseDuration < TimeSpan.FromSeconds(1)) throw new ArgumentException("A job kind and positive lease are required.");
        var now = clock.GetUtcNow();
        var until = now.Add(leaseDuration);
        var token = Guid.CreateVersion7();
        // Selection and lease update are one database statement; multiple workers skip rows already being claimed.
        var rows = await db.Jobs.FromSqlInterpolated($"""
            UPDATE ttb."Jobs" AS j
            SET "LeaseToken" = {token}, "LeaseUntil" = {until}, "Attempts" = j."Attempts" + 1
            WHERE j."Id" = (
                SELECT "Id" FROM ttb."Jobs"
                WHERE "CompletedAt" IS NULL AND "AvailableAt" <= {now}
                  AND ("LeaseUntil" IS NULL OR "LeaseUntil" <= {now}) AND "Kind" = ANY({kinds})
                ORDER BY "AvailableAt", "Id" FOR UPDATE SKIP LOCKED LIMIT 1
            ) RETURNING j.*
            """).AsNoTracking().ToListAsync(cancellationToken);
        var row = rows.SingleOrDefault();
        return row is null ? null : new(row.Id, token, row.Kind, row.PayloadJson, row.Attempts);
    }

    public async Task<bool> CompleteAsync(JobLease lease, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        return await db.Jobs.Where(x => x.Id == lease.Id && x.LeaseToken == lease.Token && x.LeaseUntil > now && x.CompletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CompletedAt, now).SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null).SetProperty(x => x.LastError, (string?)null), cancellationToken) == 1;
    }

    public async Task<bool> RetryAsync(JobLease lease, string safeError, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var next = now.Add(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
        var error = safeError[..Math.Min(safeError.Length, 2000)];
        return await db.Jobs.Where(x => x.Id == lease.Id && x.LeaseToken == lease.Token && x.LeaseUntil > now && x.CompletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AvailableAt, next).SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null).SetProperty(x => x.LastError, error), cancellationToken) == 1;
    }

    public async Task<bool> RenewAsync(JobLease lease, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (duration < TimeSpan.FromSeconds(1)) throw new ArgumentOutOfRangeException(nameof(duration));
        var now = clock.GetUtcNow();
        return await db.Jobs.Where(x => x.Id == lease.Id && x.LeaseToken == lease.Token && x.LeaseUntil > now && x.CompletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, now.Add(duration)), cancellationToken) == 1;
    }
}
