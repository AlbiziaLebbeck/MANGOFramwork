using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class Numbering(CommerceDb db, TimeProvider clock)
{
    public static readonly TimeZoneInfo Bangkok = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");

    public async Task<long> NextAsync(string scope, long maximum, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Number allocation must be part of the business transaction.");
        var id = Guid.CreateVersion7(); var now = clock.GetUtcNow();
        var values = await db.Database.SqlQuery<long>($"""
            INSERT INTO ttb."Sequences" ("Id", "Scope", "Value", "CreatedAt")
            VALUES ({id}, {scope}, 1, {now})
            ON CONFLICT ("Scope") DO UPDATE SET "Value" = ttb."Sequences"."Value" + 1
            RETURNING "Value"
            """).ToListAsync(cancellationToken);
        var value = values.Single();
        if (value > maximum) throw new CommerceException(503, "numbering_exhausted", "The current numbering window is full. Please try again later.");
        return value;
    }
}
