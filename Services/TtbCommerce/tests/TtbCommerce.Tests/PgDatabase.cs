using Microsoft.EntityFrameworkCore;
using Npgsql;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class PgFactAttribute : FactAttribute
{
    public PgFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TTB_TEST_DATABASE")))
            Skip = "Set TTB_TEST_DATABASE to an isolated development PostgreSQL connection. No in-memory replacement is accepted.";
    }
}

public sealed class PgTheoryAttribute : TheoryAttribute
{
    public PgTheoryAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TTB_TEST_DATABASE"))) Skip = "Set TTB_TEST_DATABASE to the isolated development PostgreSQL.";
    }
}

public sealed class PgDatabase : IAsyncDisposable
{
    private readonly string adminConnection;
    private readonly string name;
    public string ConnectionString { get; }

    private PgDatabase(string connection, string databaseName)
    {
        adminConnection = connection;
        name = databaseName;
        ConnectionString = new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ConnectionString;
    }

    public static async Task<PgDatabase> CreateAsync()
    {
        var connection = Environment.GetEnvironmentVariable("TTB_TEST_DATABASE") ?? throw new InvalidOperationException("No PostgreSQL test connection.");
        var source = new NpgsqlConnectionStringBuilder(connection);
        if (source.Database != "ttb_development") throw new InvalidOperationException("Integration tests require the designated ttb_development admin DB, never UAT/Production/KKU.");
        var database = new PgDatabase(connection, $"ttb_test_{Guid.NewGuid():N}");
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{database.name}\"", admin)) await command.ExecuteNonQueryAsync();
        try
        {
            await using var db = database.Open();
            await db.Database.MigrateAsync();
            return database;
        }
        catch { await database.DisposeAsync(); throw; }
    }

    public CommerceDb Open() => new(new DbContextOptionsBuilder<CommerceDb>().UseNpgsql(ConnectionString).Options);

    public async ValueTask DisposeAsync()
    {
        // Only the random database created by this instance can be removed; never the configured admin database.
        if (!name.StartsWith("ttb_test_", StringComparison.Ordinal) || name.Length != 41 || name[9..].Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidOperationException("Refusing to remove an unexpected database.");
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class MutableClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset current = now.ToUniversalTime();
    public override DateTimeOffset GetUtcNow() => current;
    public void Advance(TimeSpan duration) => current = current.Add(duration);
}
