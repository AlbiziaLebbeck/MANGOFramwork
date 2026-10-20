using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public static class BankReadBudget
{
    public static async Task ReserveAsync(CommerceDb db, string merchant, int limit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Reserve the bank request rate within its durable intent transaction.");
        var scope = $"bank-read:{merchant}:{now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}"; var id = Guid.CreateVersion7();
        var values = await db.Database.SqlQuery<long>($"""
            INSERT INTO ttb."Sequences" ("Id", "Scope", "Value", "CreatedAt") VALUES ({id}, {scope}, 1, {now})
            ON CONFLICT ("Scope") DO UPDATE SET "Value" = ttb."Sequences"."Value" + 1 WHERE ttb."Sequences"."Value" < {limit}
            RETURNING "Value"
            """).ToListAsync(cancellationToken);
        if (values.Count == 0) throw new CommerceException(429, "bank_rate_limited", "The bank query capacity for this minute has been reached. Please wait.");
    }
}
