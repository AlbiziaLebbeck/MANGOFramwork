using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class PrivateFiles(PrivateStorageOptions options)
{
    public string Root { get; } = Path.GetFullPath(string.IsNullOrWhiteSpace(options.RootPath) ? Path.Combine(AppContext.BaseDirectory, ".data") : options.RootPath);

    public string PathFor(string area, string key, string extension)
    {
        if (area is not ("assets" or "documents" or "reports" or "finance") || !Regex.IsMatch(key, "^[a-z0-9_-]{1,100}$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(extension, "^\\.[a-z0-9]{1,10}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid private storage identifier.");
        var directory = Path.Combine(Root, area); Directory.CreateDirectory(directory);
        return Path.Combine(directory, key + extension);
    }

    public async Task AssertAssetAsync(DigitalAsset asset, CancellationToken cancellationToken)
    {
        var path = PathFor("assets", asset.Key, ".asset");
        if (!asset.Ready || !File.Exists(path) || new FileInfo(path).Length != asset.ByteLength) throw Unavailable();
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        if (hash != asset.Sha256) throw Unavailable();
    }

    public static CommerceException Unavailable() => new(409, "digital_asset_unavailable", "The private digital asset is temporarily unavailable.");
}

public sealed class DigitalAssetService(CommerceDb db, PrivateFiles files, PrivateStorageOptions options)
{
    public async Task<DigitalAsset> UploadAsync(Stream input, string fileName, string actor, CancellationToken cancellationToken)
    {
        fileName = fileName.Replace('\\', '/').Split('/').Last();
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 200 || fileName.Any(char.IsControl) ||
            Path.GetExtension(fileName).ToLowerInvariant() is not (".glb" or ".gltf" or ".zip" or ".pdf" or ".png" or ".jpg" or ".webp" or ".fbx" or ".obj" or ".txt"))
            throw new CommerceException(400, "asset_file_invalid", "Use a supported asset filename.");
        var key = Guid.NewGuid().ToString("N"); var temporary = files.PathFor("assets", key, ".upload"); var final = files.PathFor("assets", key, ".asset");
        var databaseWriteStarted = false;
        try
        {
            long bytes = 0; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920]; int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    bytes = checked(bytes + read); if (bytes > options.MaxAssetBytes) throw new CommerceException(413, "asset_too_large", "The asset exceeds the allowed size.");
                    hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
            }
            if (bytes == 0) throw new CommerceException(400, "asset_empty", "Upload a nonempty asset.");
            File.Move(temporary, final, overwrite: false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(final, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            var asset = new DigitalAsset { Key = key, FileName = fileName, ByteLength = bytes, Sha256 = Convert.ToHexStringLower(hash.GetHashAndReset()), Ready = true, UploadedBy = actor };
            db.DigitalAssets.Add(asset); db.AuditEvents.Add(new AuditEvent { Actor = actor, Action = "digital-asset.upload", Resource = $"asset:{key}" });
            databaseWriteStarted = true;
            await db.SaveChangesAsync(cancellationToken); return asset;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            // A lost DB commit acknowledgement is ambiguous: keep the immutable file; an orphan sweep can reconcile it later.
            if (!databaseWriteStarted && File.Exists(final)) File.Delete(final);
        }
    }

    public Task<DigitalAsset[]> ListAsync(CancellationToken cancellationToken) => db.DigitalAssets.AsNoTracking().OrderByDescending(x => x.Id).Take(100).ToArrayAsync(cancellationToken);
}
