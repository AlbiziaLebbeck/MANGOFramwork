using TtbCommerce.Infrastructure;

namespace TtbCommerce.Api;

public readonly record struct PrivateStorageCapacity(bool Available, long TotalBytes, long AvailableBytes);

public static class PrivateStorageCapacityProbe
{
    // Read filesystem capacity only. Never enumerate private files, derive labels from paths or create a missing directory.
    public static PrivateStorageCapacity Read(PrivateFiles files)
    {
        try
        {
            if (!Directory.Exists(files.Root)) return default;
            // Unix DriveInfo uses statvfs for this exact path, including a bound PVC. Windows uses its drive root.
            var drive = new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(files.Root)! : files.Root);
            if (!drive.IsReady) return default;
            var total = drive.TotalSize; var available = drive.AvailableFreeSpace;
            return total > 0 && available >= 0 && available <= total ? new(true, total, available) : default;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException or OverflowException)
        {
            return default;
        }
    }
}
