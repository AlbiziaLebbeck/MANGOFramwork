using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class DatabaseTests
{
    [PgFact]
    public async Task Populated_financial_rollback_is_refused_and_forward_recovery_preserves_pending_orders_and_effect_keys()
    {
        await using var database = await PgDatabase.CreateAsync(); using var storage = new TestPrivateStorage();
        var paid = await PaymentFixture.CreateAsync(database); var pending = await PaymentFixture.CreateAsync(database);
        await using var db = database.Open();
        await PaymentFixture.Processor(db).NoticeAsync(paid.Raw, PaymentFixture.Metadata(), default);
        var credit = await db.BankTransactions.AsNoTracking().SingleAsync();
        var officer = new ShopperActor(Guid.NewGuid(), "mangosgo", "rollback-officer", "Rollback test", ["finance"]);
        var refund = await new ManualRefundService(db, PaymentFixture.Commerce, PaymentFixture.Bank, new(), TimeProvider.System, storage.Files)
            .CreateAsync(credit.Id, new(Guid.NewGuid().ToString("D"), "100", "Synthetic rollback protection test"), officer, default);
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(x => x.OrderId == pending.OrderId);
        var keys = await db.Jobs.AsNoTracking().OrderBy(x => x.EffectKey).Select(x => x.EffectKey).ToArrayAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261018151649_ExtraCreditVoidDocuments"));
        Assert.Equal("P0001", error.SqlState);
        Assert.Contains("Refusing to discard financial/return evidence", error.MessageText, StringComparison.Ordinal);
        db.ChangeTracker.Clear();
        Assert.Equal(refund.Id, (await db.ManualRefunds.SingleAsync()).Id);
        Assert.Equal(credit.AmountMinor, (await db.BankTransactions.SingleAsync()).AmountMinor);
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var restoredAttempt = await db.Attempts.SingleAsync(x => x.OrderId == pending.OrderId);
        Assert.Equal(attempt.Id, restoredAttempt.Id); Assert.Equal(attempt.PayloadHash, restoredAttempt.PayloadHash);
        Assert.Equal(PaymentState.Unpaid, (await db.Orders.SingleAsync(x => x.Id == pending.OrderId)).PaymentState);
        Assert.Equal(keys, await db.Jobs.AsNoTracking().OrderBy(x => x.EffectKey).Select(x => x.EffectKey).ToArrayAsync());
        Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Equal(2, await db.Orders.CountAsync());
        Assert.Equal(1, (await db.Variants.SingleAsync(x => x.Id == pending.VariantId)).Reserved);
    }

    [PgFact]
    public async Task Migrations_match_model_and_round_trip_on_empty_test_database()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using var db = database.Open();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await db.GetService<IMigrator>().MigrateAsync("0");
        await db.Database.MigrateAsync();
        Assert.Equal(0, await db.Orders.CountAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [PgFact]
    public async Task Wrong_environment_is_rejected_even_when_identifiers_would_match()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using (var db = database.Open()) await DbGuard.AssertEnvironmentAsync(db, CommerceEnvironment.Uat);
        await using (var db = database.Open()) await DbGuard.AssertEnvironmentAsync(db, CommerceEnvironment.Uat);
        await using (var db = database.Open())
            await Assert.ThrowsAsync<InvalidOperationException>(() => DbGuard.AssertEnvironmentAsync(db, CommerceEnvironment.Production));
    }

    [PgFact]
    public async Task Concurrent_workers_claim_each_job_once_and_retry_is_durable()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using (var db = database.Open())
        {
            for (var i = 0; i < 12; i++) db.Jobs.Add(DurableWork.Enqueue($"effect:{i}", "test", "{}"));
            await db.SaveChangesAsync();
        }
        var leases = await Task.WhenAll(Enumerable.Range(0, 24).Select(async _ =>
        {
            await using var db = database.Open();
            return await new DurableWork(db, TimeProvider.System).ClaimAsync(["test"], TimeSpan.FromMinutes(1));
        }));
        var claimed = leases.OfType<JobLease>().ToArray();
        Assert.Equal(12, claimed.Length);
        Assert.Equal(12, claimed.Select(x => x.Id).Distinct().Count());
        await using (var db = database.Open())
        {
            var worker = new DurableWork(db, TimeProvider.System);
            Assert.True(await worker.RetryAsync(claimed[0], "safe-business-error", TimeSpan.Zero));
            Assert.False(await worker.CompleteAsync(claimed[0]));
        }
        await using (var db = database.Open())
        {
            var worker = new DurableWork(db, TimeProvider.System);
            var retry = Assert.IsType<JobLease>(await worker.ClaimAsync(["test"], TimeSpan.FromMinutes(1)));
            Assert.Equal(claimed[0].Id, retry.Id);
            Assert.Equal(2, retry.Attempts);
            Assert.True(await worker.CompleteAsync(retry));
            Assert.Null(await worker.ClaimAsync(["test"], TimeSpan.FromMinutes(1)));
        }
    }

    [PgFact]
    public async Task Expired_lease_cannot_complete_or_renew_after_another_worker_recovers_it()
    {
        await using var database = await PgDatabase.CreateAsync();
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(1));
        await using var db = database.Open();
        db.Jobs.Add(DurableWork.Enqueue("recover", "test", "{}"));
        await db.SaveChangesAsync();
        var worker = new DurableWork(db, clock);
        var original = Assert.IsType<JobLease>(await worker.ClaimAsync(["test"], TimeSpan.FromSeconds(10)));
        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.False(await worker.CompleteAsync(original));
        var recovered = Assert.IsType<JobLease>(await worker.ClaimAsync(["test"], TimeSpan.FromSeconds(30)));
        Assert.NotEqual(original.Token, recovered.Token);
        Assert.False(await worker.RenewAsync(original, TimeSpan.FromMinutes(1)));
        Assert.False(await worker.RetryAsync(original, "obsolete-worker", TimeSpan.Zero));
        Assert.True(await worker.CompleteAsync(recovered));
    }

    [PgFact]
    public async Task Duplicate_effect_key_rejects_the_entire_transaction()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using (var db = database.Open())
        {
            db.Jobs.Add(DurableWork.Enqueue("one-effect", "test", "{}"));
            await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.AuditEvents.Add(new AuditEvent { Actor = "test", Action = "rolled-back", Resource = "test" });
            db.Jobs.Add(DurableWork.Enqueue("one-effect", "test", "{}"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
        await using (var db = database.Open())
        {
            Assert.Equal(1, await db.Jobs.CountAsync());
            Assert.Equal(0, await db.AuditEvents.CountAsync());
        }
    }

    [PgFact]
    public async Task Bank_instruction_identity_is_per_environment_merchant_and_biller()
    {
        await using var database = await PgDatabase.CreateAsync();
        await using (var db = database.Open())
        {
            db.BankTransactions.Add(new BankTransaction { Environment = CommerceEnvironment.Uat, MerchantId = "TST", BillerNo = "111111111111111", InstructionId = "SAME" });
            db.BankTransactions.Add(new BankTransaction { Environment = CommerceEnvironment.Production, MerchantId = "TST", BillerNo = "111111111111111", InstructionId = "SAME" });
            await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
        {
            db.BankTransactions.Add(new BankTransaction { Environment = CommerceEnvironment.Uat, MerchantId = "TST", BillerNo = "111111111111111", InstructionId = "SAME" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}
