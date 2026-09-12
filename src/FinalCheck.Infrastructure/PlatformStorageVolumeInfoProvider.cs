using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

public sealed class PlatformStorageVolumeInfoProvider : IStorageVolumeInfoProvider
{
    private static readonly string[] SyncEnvironmentNames = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];
    public StorageVolumeInfo Inspect(string absolutePath)
    {
        var drive = new DriveInfo(Path.GetPathRoot(absolutePath)!);
        var kind = drive.DriveType switch
        {
            DriveType.Fixed => StorageDriveKind.Fixed,
            DriveType.Network => StorageDriveKind.Network,
            DriveType.Removable or DriveType.CDRom => StorageDriveKind.Removable,
            _ => StorageDriveKind.Unknown,
        };
        var knownSync = SyncEnvironmentNames
            .Select(Environment.GetEnvironmentVariable).Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
            .Select(path => Path.GetFullPath(path!)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(kind, drive.AvailableFreeSpace, Path.GetTempPath(), knownSync);
    }
}
