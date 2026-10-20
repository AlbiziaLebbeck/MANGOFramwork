using System.Security.Cryptography;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Renci.SshNet;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public interface IReportSource { Task<byte[]?> DownloadAsync(string name, CancellationToken cancellationToken); }
public interface IReportDecryptor { byte[] Decrypt(byte[] encrypted, ReportCryptoProfile? archiveProfile = null); }

public sealed class TtbReportSource(CommerceOptions commerce, ReportOptions options) : IReportSource
{
    public async Task<byte[]?> DownloadAsync(string name, CancellationToken cancellationToken)
    {
        ReportParser.FileName(name);
        if (!options.Enabled || commerce.Environment == CommerceEnvironment.Local || options.Missing().Count > 0) throw ReportParser.Error("transport_unavailable");
        var connection = new ConnectionInfo(TtbOptions.Gateway(commerce.Environment).Host, options.Port, options.Username,
            new PasswordAuthenticationMethod(options.Username, options.Password)) { Timeout = TimeSpan.FromSeconds(15) };
        using var client = new SftpClient(connection) { OperationTimeout = TimeSpan.FromSeconds(20), KeepAliveInterval = TimeSpan.FromSeconds(10) };
        client.HostKeyReceived += (_, args) => args.CanTrust = TrustedHostKey(args.HostKey, options.HostKeySha256);
        await client.ConnectAsync(cancellationToken);
        var path = "/QRTRAN/report/" + name + options.RemoteSuffix;
        if (!client.Exists(path)) return null;
        var before = client.GetAttributes(path);
        if (before.IsSymbolicLink || before.IsDirectory || before.Size is < 1 || before.Size > options.MaxFileBytes) throw ReportParser.Error("remote_file_size_type");
        using var input = client.OpenRead(path);
        var result = await BoundedRead.Async(input, options.MaxFileBytes, cancellationToken);
        var after = client.GetAttributes(path);
        if (result.Length != before.Size || after.Size != before.Size || after.LastWriteTimeUtc != before.LastWriteTimeUtc) throw ReportParser.Error("remote_file_changed");
        return result;
    }

    public static bool TrustedHostKey(byte[] key, string expected)
    {
        var actual = "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(actual), System.Text.Encoding.ASCII.GetBytes(expected));
    }
}

public sealed class PgpReportDecryptor(ReportOptions options) : IReportDecryptor
{
    public byte[] Decrypt(byte[] encrypted, ReportCryptoProfile? archiveProfile = null)
    {
        var profile = TrustedProfile(archiveProfile);
        if (encrypted.Length > options.MaxFileBytes || !File.Exists(options.PgpPrivateKeyPath)) throw ReportParser.Error("pgp_key_or_size");
        try
        {
            using var input = PgpUtilities.GetDecoderStream(new MemoryStream(encrypted, writable: false));
            var factory = new PgpObjectFactory(input);
            var first = factory.NextPgpObject();
            if (first is PgpMarker) first = factory.NextPgpObject();
            if (first is not PgpEncryptedDataList list) throw ReportParser.Error("pgp_encryption_required");
            using var secretInput = PgpUtilities.GetDecoderStream(File.OpenRead(options.PgpPrivateKeyPath));
            var keys = new PgpSecretKeyRingBundle(secretInput);
            PgpPublicKeyEncryptedData? packet = null; PgpPrivateKey? privateKey = null;
            var passphrase = options.PgpPassphrase.ToCharArray();
            try
            {
                foreach (var item in list.GetEncryptedDataObjects())
                    if (item is PgpPublicKeyEncryptedData candidate && keys.GetSecretKey(candidate.KeyId) is { } secret &&
                        Convert.ToHexString(secret.PublicKey.GetFingerprint()).Equals(profile.RecipientFingerprint, StringComparison.OrdinalIgnoreCase))
                    { packet = candidate; privateKey = secret.ExtractPrivateKey(passphrase); break; }
            }
            finally { Array.Clear(passphrase); }
            if (packet is null || privateKey is null || !packet.IsIntegrityProtected() || packet.GetSymmetricAlgorithm(privateKey) != SymmetricKeyAlgorithmTag.Aes256)
                throw ReportParser.Error("pgp_recipient_cipher_integrity");
            using var clear = packet.GetDataStream(privateKey);
            // Bound the complete decrypted packet stream before parsing, as well as decompressed literal data.
            using var clearPackets = new MemoryStream(BoundedRead.Sync(clear, options.MaxFileBytes), writable: false);
            if (!packet.Verify() || factory.NextPgpObject() is not null) throw ReportParser.Error("pgp_integrity");
            var messageFactory = new PgpObjectFactory(clearPackets);
            var message = messageFactory.NextPgpObject();
            var depth = 0;
            while (message is PgpCompressedData compressed)
            {
                if (++depth > 2) throw ReportParser.Error("pgp_compression_depth");
                using var decompressed = compressed.GetDataStream();
                var buffer = BoundedRead.Sync(decompressed, options.MaxFileBytes);
                if (messageFactory.NextPgpObject() is not null) throw ReportParser.Error("pgp_extra_packets");
                messageFactory = new PgpObjectFactory(buffer); message = messageFactory.NextPgpObject();
            }
            PgpOnePassSignature? signature = null;
            if (message is PgpOnePassSignatureList signatures && signatures.Count == 1)
            {
                signature = signatures[0];
                if (signature.HashAlgorithm is not (HashAlgorithmTag.Sha256 or HashAlgorithmTag.Sha384 or HashAlgorithmTag.Sha512) || signature.SignatureType != PgpSignature.BinaryDocument)
                    throw ReportParser.Error("pgp_signature_algorithm");
                using var publicInput = PgpUtilities.GetDecoderStream(File.OpenRead(options.SigningPublicKeyPath));
                var publicKey = new PgpPublicKeyRingBundle(publicInput).GetPublicKey(signature.KeyId);
                if (publicKey is null || !Convert.ToHexString(publicKey.GetFingerprint()).Equals(profile.SigningFingerprint, StringComparison.OrdinalIgnoreCase)) throw ReportParser.Error("pgp_signer");
                signature.InitVerify(publicKey); message = messageFactory.NextPgpObject();
            }
            if (profile.SigningFingerprint.Length > 0 && signature is null) throw ReportParser.Error("pgp_signature_required");
            if (message is not PgpLiteralData literal) throw ReportParser.Error("pgp_literal");
            // The embedded filename is untrusted metadata. Never create or choose a path from it.
            using var payload = literal.GetInputStream(); var plain = BoundedRead.Sync(payload, options.MaxFileBytes);
            if (signature is not null)
            {
                signature.Update(plain);
                if (messageFactory.NextPgpObject() is not PgpSignatureList result || result.Count != 1 || !signature.Verify(result[0])) throw ReportParser.Error("pgp_signature");
            }
            if (messageFactory.NextPgpObject() is not null) throw ReportParser.Error("pgp_extra_packets");
            return plain;
        }
        catch (CommerceException) { throw; }
        catch (Exception exception) when (exception is PgpException or IOException or ArgumentException or InvalidOperationException or Org.BouncyCastle.Crypto.CryptoException)
        { throw ReportParser.Error("pgp_decryption"); }
    }

    private ReportCryptoProfile TrustedProfile(ReportCryptoProfile? archive)
    {
        if (options.MissingArchiveTrust().Count > 0 || !ReportOptions.Fingerprint(options.RecipientFingerprint) ||
            options.SigningFingerprint.Length > 0 && !ReportOptions.Fingerprint(options.SigningFingerprint)) throw ReportParser.Error("pgp_trust_profile");
        var profile = archive ?? new(options.RecipientFingerprint, options.SigningFingerprint);
        if (!ReportOptions.Fingerprint(profile.RecipientFingerprint) || profile.SigningFingerprint is null ||
            profile.SigningFingerprint.Length > 0 && !ReportOptions.Fingerprint(profile.SigningFingerprint)) throw ReportParser.Error("pgp_archive_profile");
        bool Allowed(string pin, string active, string[] retired) => pin.Equals(active, StringComparison.OrdinalIgnoreCase) ||
            archive is not null && retired.Contains(pin, StringComparer.OrdinalIgnoreCase);
        // Strengthening the current signing policy cannot be bypassed by an unsigned historical profile.
        if (options.SigningFingerprint.Length > 0 && profile.SigningFingerprint.Length == 0) throw ReportParser.Error("pgp_signature_required");
        if (!Allowed(profile.RecipientFingerprint, options.RecipientFingerprint, options.ArchiveRecipientFingerprints) ||
            !Allowed(profile.SigningFingerprint, options.SigningFingerprint, options.ArchiveSigningFingerprints)) throw ReportParser.Error("pgp_archive_key_unapproved");
        return profile;
    }
}

internal static class BoundedRead
{
    public static byte[] Sync(Stream source, int maximum)
    {
        using var output = new MemoryStream(); var buffer = new byte[16384]; int count;
        while ((count = source.Read(buffer)) > 0) { if (output.Length + count > maximum) throw ReportParser.Error("size"); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
    public static async Task<byte[]> Async(Stream source, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(); var buffer = new byte[16384]; int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0) { if (output.Length + count > maximum) throw ReportParser.Error("size"); await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken); }
        return output.ToArray();
    }
}
