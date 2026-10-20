using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

// Genuine OpenPGP packets and two locally generated keys. No bank material or connection.
internal sealed class ReportRotationFixture : IDisposable
{
    public TestPrivateStorage Storage { get; } = new();
    public ReportOptions Options { get; } = ReportFixtures.Profile();
    public PgpSecretKey Old { get; }
    public PgpSecretKey Current { get; }
    private readonly SecureRandom random = new();
    public static string Fingerprint(PgpSecretKey key) => Convert.ToHexString(key.PublicKey.GetFingerprint());
    public ReportCryptoProfile OldProfile => new(Fingerprint(Old), Fingerprint(Old));

    public ReportRotationFixture()
    {
        Old = Key("Old synthetic report key"); Current = Key("Current synthetic report key");
        Directory.CreateDirectory(Storage.Root);
        Options.PgpPrivateKeyPath = Path.Combine(Storage.Root, "secret-ring.pgp");
        Options.SigningPublicKeyPath = Path.Combine(Storage.Root, "public-ring.pgp");
        File.WriteAllBytes(Options.PgpPrivateKeyPath, [.. Old.GetEncoded(), .. Current.GetEncoded()]);
        File.WriteAllBytes(Options.SigningPublicKeyPath, [.. Old.PublicKey.GetEncoded(), .. Current.PublicKey.GetEncoded()]);
        Options.PgpPassphrase = "synthetic-test-only";
        Options.RecipientFingerprint = Fingerprint(Old); Options.SigningFingerprint = Fingerprint(Old);
    }

    public void Rotate(bool allowArchived = false)
    {
        Options.RecipientFingerprint = Fingerprint(Current); Options.SigningFingerprint = Fingerprint(Current);
        Options.ArchiveRecipientFingerprints = allowArchived ? [Fingerprint(Old)] : [];
        Options.ArchiveSigningFingerprints = allowArchived ? [Fingerprint(Old)] : [];
    }

    public byte[] Encrypt(byte[] plain, PgpSecretKey recipient, PgpSecretKey? signer)
    {
        using var packets = new MemoryStream(); PgpSignatureGenerator? signature = null;
        if (signer is not null)
        {
            signature = new(signer.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
            signature.InitSign(PgpSignature.BinaryDocument, signer.ExtractPrivateKey(Options.PgpPassphrase.ToCharArray()));
            signature.GenerateOnePassVersion(false).Encode(packets);
        }
        var literal = new PgpLiteralDataGenerator();
        using (var data = literal.Open(packets, PgpLiteralData.Binary, "untrusted-name", plain.Length, DateTime.UtcNow)) data.Write(plain);
        if (signature is not null) { signature.Update(plain); signature.Generate().Encode(packets); }
        using var output = new MemoryStream(); var encryption = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Aes256, true, random);
        encryption.AddMethod(recipient.PublicKey);
        using (var data = encryption.Open(output, packets.Length)) data.Write(packets.ToArray());
        return output.ToArray();
    }

    public ReportService Service(CommerceDb db) => new(db, PaymentFixture.Commerce, PaymentFixture.Bank, Options, Storage.Files,
        new PgpReportDecryptor(Options), TimeProvider.System);

    private PgpSecretKey Key(string name)
    {
        var generator = new RsaKeyPairGenerator(); generator.Init(new KeyGenerationParameters(random, 2048));
        var pair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaGeneral, generator.GenerateKeyPair(), DateTime.UtcNow);
        return new(PgpSignature.DefaultCertification, pair, name, SymmetricKeyAlgorithmTag.Aes256, "synthetic-test-only".ToCharArray(), true, null, null, random);
    }
    public void Dispose() => Storage.Dispose();
}

public sealed class ReportRotationTests
{
    [Fact]
    public void New_files_accept_only_active_recipient_and_signer_even_when_retired_keys_are_retained()
    {
        using var fixture = new ReportRotationFixture(); var plain = "synthetic report bytes"u8.ToArray();
        var old = fixture.Encrypt(plain, fixture.Old, fixture.Old); var decryptor = new PgpReportDecryptor(fixture.Options);
        Assert.Equal(plain, decryptor.Decrypt(old)); fixture.Rotate(allowArchived: true);
        Assert.Equal(plain, decryptor.Decrypt(fixture.Encrypt(plain, fixture.Current, fixture.Current)));
        Assert.Throws<CommerceException>(() => decryptor.Decrypt(old));
        Assert.Throws<CommerceException>(() => decryptor.Decrypt(fixture.Encrypt(plain, fixture.Old, fixture.Current)));
        Assert.Equal("report_pgp_signer", Assert.Throws<CommerceException>(() => decryptor.Decrypt(fixture.Encrypt(plain, fixture.Current, fixture.Old))).Code);
    }

    [Fact]
    public void Archived_ciphertext_requires_both_explicit_retired_pins_and_original_exact_profile()
    {
        using var fixture = new ReportRotationFixture(); var plain = RandomNumberGenerator.GetBytes(200);
        var encrypted = fixture.Encrypt(plain, fixture.Old, fixture.Old); var decryptor = new PgpReportDecryptor(fixture.Options);
        fixture.Rotate();
        Assert.Equal("report_pgp_archive_key_unapproved", Assert.Throws<CommerceException>(() => decryptor.Decrypt(encrypted, fixture.OldProfile)).Code);
        fixture.Options.ArchiveRecipientFingerprints = [ReportRotationFixture.Fingerprint(fixture.Old)];
        Assert.Equal("report_pgp_archive_key_unapproved", Assert.Throws<CommerceException>(() => decryptor.Decrypt(encrypted, fixture.OldProfile)).Code);
        fixture.Options.ArchiveSigningFingerprints = [ReportRotationFixture.Fingerprint(fixture.Old).ToLowerInvariant()];
        Assert.Equal(plain, decryptor.Decrypt(encrypted, fixture.OldProfile));
        Assert.Throws<CommerceException>(() => decryptor.Decrypt(encrypted, new(new string('F', 40), fixture.OldProfile.SigningFingerprint)));
        var damaged = encrypted.ToArray(); damaged[^5] ^= 1;
        Assert.Throws<CommerceException>(() => decryptor.Decrypt(damaged, fixture.OldProfile));
        fixture.Options.ArchiveRecipientFingerprints = [];
        Assert.Throws<CommerceException>(() => decryptor.Decrypt(encrypted, fixture.OldProfile));
    }

    [Fact]
    public void An_unsigned_archive_cannot_bypass_a_new_required_signing_policy()
    {
        using var fixture = new ReportRotationFixture(); var plain = "unsigned synthetic bytes"u8.ToArray();
        var encrypted = fixture.Encrypt(plain, fixture.Old, null); fixture.Rotate(allowArchived: true);
        var profile = fixture.OldProfile with { SigningFingerprint = "" };
        Assert.Equal("report_pgp_signature_required", Assert.Throws<CommerceException>(() => new PgpReportDecryptor(fixture.Options).Decrypt(encrypted, profile)).Code);
    }

    [Fact]
    public void Retired_trust_rejects_wildcards_duplicates_nulls_active_pins_and_unbounded_lists()
    {
        var options = ReportFixtures.Profile(); options.RecipientFingerprint = new string('A', 40); options.SigningFingerprint = new string('B', 40); options.SigningPublicKeyPath = "public-only";
        foreach (var pins in new string[][] { ["*"], [new string('C', 40), new string('c', 40)], [null!], [options.RecipientFingerprint],
                     Enumerable.Range(0, 9).Select(x => x.ToString("X40")).ToArray(), null! })
        {
            options.ArchiveRecipientFingerprints = pins;
            Assert.Contains("Reports.ArchiveRecipientFingerprints", options.MissingArchiveTrust());
        }
        options.ArchiveRecipientFingerprints = [new string('C', 40)];
        options.ArchiveSigningFingerprints = [new string('D', 40)]; Assert.Empty(options.MissingArchiveTrust());
        options.SigningFingerprint = ""; Assert.Contains("Reports.ArchiveSigningFingerprints", options.MissingArchiveTrust());
    }

    [PgFact]
    public async Task Audited_repair_after_rotation_uses_immutable_old_pins_and_never_duplicates_money_or_accepts_new_old_key_files()
    {
        await using var database = await PgDatabase.CreateAsync(); using var fixture = new ReportRotationFixture();
        var (payment, _) = await ReportFixtures.PaidAsync(database);
        var plain = ReportFixtures.File(fixture.Options, ReportKind.Iso, ReportFixtures.From(payment));
        var encrypted = fixture.Encrypt(plain, fixture.Old, fixture.Old);
        fixture.Options.MerchantAccount = "9999999999"; Guid id; string original;
        await using (var db = database.Open())
        {
            var service = fixture.Service(db); var archive = await service.ImportAsync(ReportFixtures.IsoName, encrypted, default);
            id = archive.Id; original = archive.ParserProfileJson; await service.StageAsync(id, false, default);
            Assert.Equal("report_beneficiary", (await db.Reports.AsNoTracking().SingleAsync()).FailureCode);
            Assert.Equal(0, await db.ReportRows.CountAsync());
        }
        fixture.Rotate(allowArchived: true); fixture.Options.MerchantAccount = "1234567890";
        await using (var db = database.Open())
        {
            var service = fixture.Service(db); await service.ReprocessAsync(id, "finance:rotation-test", "Validated receiving account after planned key rotation", default);
            await service.StageAsync(id, true, default); var rowId = await db.ReportRows.Select(x => x.Id).SingleAsync();
            await service.ReconcileAsync(rowId, default); await service.ReconcileAsync(rowId, default);
            var archive = await db.Reports.AsNoTracking().SingleAsync(); Assert.Equal("Complete", archive.State); Assert.Equal(original, archive.ParserProfileJson);
            using var applied = JsonDocument.Parse(archive.AppliedProfileJson);
            Assert.Equal(fixture.OldProfile.RecipientFingerprint, applied.RootElement.GetProperty("crypto").GetProperty("recipientFingerprint").GetString());
            Assert.Equal(fixture.OldProfile.SigningFingerprint, applied.RootElement.GetProperty("crypto").GetProperty("signingFingerprint").GetString());
            Assert.Equal(encrypted, await File.ReadAllBytesAsync(fixture.Storage.Files.PathFor("reports", archive.FileKey, ".pgp")));
            Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.Action == "report.reprocess"));
            Assert.Equal(1, await db.Allocations.CountAsync()); Assert.Equal(1, await db.BankTransactions.CountAsync());
            Assert.Equal(1, await db.StockMovements.CountAsync(x => x.EffectKey.StartsWith("commit:")));
            var rejected = await service.ImportAsync(ReportFixtures.IsoName, fixture.Encrypt(plain, fixture.Old, fixture.Old), default);
            await service.StageAsync(rejected.Id, false, default);
            Assert.Equal("Quarantined", (await db.Reports.AsNoTracking().SingleAsync(x => x.Id == rejected.Id)).State);
            var reencrypted = await service.ImportAsync(ReportFixtures.IsoName, fixture.Encrypt(plain, fixture.Current, fixture.Current), default);
            await service.StageAsync(reencrypted.Id, false, default);
            Assert.Equal("Replay", (await db.Reports.AsNoTracking().SingleAsync(x => x.Id == reencrypted.Id)).State);
            Assert.Equal(1, await db.ReportRows.CountAsync()); Assert.Equal(1, await db.Allocations.CountAsync());
        }
    }

    [PgFact]
    public async Task Legacy_archives_without_crypto_snapshot_use_current_pins_only_and_never_guess_retired_keys()
    {
        await using var database = await PgDatabase.CreateAsync(); using var fixture = new ReportRotationFixture();
        var plain = ReportFixtures.File(fixture.Options, ReportKind.Iso, ReportFixtures.Detail());
        await using var db = database.Open(); var service = fixture.Service(db);
        var archive = await service.ImportAsync(ReportFixtures.IsoName, fixture.Encrypt(plain, fixture.Old, fixture.Old), default);
        // Simulate an archive produced by the previous binary, which never recorded crypto pins.
        await db.Reports.Where(x => x.Id == archive.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ParserProfileJson, "{}"));
        fixture.Rotate(allowArchived: true); await service.StageAsync(archive.Id, false, default);
        Assert.Equal("Quarantined", (await db.Reports.AsNoTracking().SingleAsync()).State);
        Assert.Equal(0, await db.ReportRows.CountAsync()); Assert.Equal(0, await db.Allocations.CountAsync());
    }
}
