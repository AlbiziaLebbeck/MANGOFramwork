using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

// A test profile cannot approve a legal issuer or alter the production deployment profile.
public sealed class IssuerProfileService(CommerceDb db, CommerceOptions commerce, ReceiptOptions defaults, TimeProvider clock)
{
    public async Task<IssuerProfileView> GetAsync(CancellationToken cancellationToken)
    {
        if (commerce.Environment == CommerceEnvironment.Production) return DefaultView(false);
        var row = await db.TestIssuerProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Key == TestIssuerProfile.SingletonKey, cancellationToken);
        return row is null ? DefaultView(true) : View(row);
    }

    public async Task<IssuerProfileView> SaveAsync(IssuerProfileInput input, string actor, CancellationToken cancellationToken)
    {
        if (commerce.Environment == CommerceEnvironment.Production)
            throw new CommerceException(409, "issuer_deployment_managed", "The approved production issuer is managed through deployment configuration.");
        var name = Text(input.Name, 200); var address = Text(input.Address, 1000, true);
        var tax = Text(input.TaxId, 13); var branch = Text(input.Branch, 100); var contact = Text(input.Contact, 300);
        if (input.ExpectedVersion < 0 || tax.Length != 13 || tax.Any(c => c is < '0' or > '9')) throw Invalid();
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Issuer profile editing must own its transaction.");
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = TestIssuerProfile.SingletonKey;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var row = await db.TestIssuerProfiles.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if ((row?.Version ?? 0) != input.ExpectedVersion)
            throw new CommerceException(409, "issuer_profile_changed", "The issuer profile changed. Reload it before saving again.");
        if (row is null) { row = new(); db.TestIssuerProfiles.Add(row); }
        else row.Version++;
        row.Name = name; row.Address = address; row.TaxId = tax; row.Branch = branch; row.Contact = contact; row.UpdatedAt = clock.GetUtcNow();
        db.AuditEvents.Add(new AuditEvent { Actor = actor, Action = "test-issuer.update", Resource = key,
            DataJson = JsonSerializer.Serialize(new { row.Version, profileHash = SecretTokens.Hash(JsonSerializer.Serialize(input)) }) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return View(row);
    }

    public async Task<ReceiptIssuer> SnapshotAsync(CancellationToken cancellationToken)
    {
        var profile = await GetAsync(cancellationToken);
        // Keep the existing receipt-v1 shape and hash contract; these display details are frozen in Address.
        var address = profile.Address + (string.IsNullOrWhiteSpace(profile.Branch) ? "" : "\nสาขา: " + profile.Branch)
            + (string.IsNullOrWhiteSpace(profile.Contact) ? "" : "\nติดต่อ: " + profile.Contact);
        return new(profile.Name, address, profile.TaxId, profile.Approved);
    }

    private IssuerProfileView DefaultView(bool editable) => new(0, defaults.IssuerName, defaults.IssuerAddress, defaults.TaxId, defaults.Branch, defaults.Contact,
        editable, commerce.Environment == CommerceEnvironment.Production && defaults.CompanyProfileApproved);
    private static IssuerProfileView View(TestIssuerProfile row) => new(row.Version, row.Name, row.Address, row.TaxId, row.Branch, row.Contact, true, false);
    private static string Text(string? input, int max, bool multiline = false)
    {
        var value = input?.Trim() ?? "";
        if (value.Length == 0 || value.Length > max || value.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r'))) throw Invalid();
        return value;
    }
    private static CommerceException Invalid() => new(400, "invalid_issuer_profile", "Complete the test issuer profile within the field limits; the tax number must contain 13 digits.");
}
