using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

public interface IStorageDirectoryProbe
{
    void Verify(string absoluteDirectory);
}

public sealed class StorageDirectoryProbe : IStorageDirectoryProbe
{
    public void Verify(string absoluteDirectory)
    {
        StorageFileSafety.RejectLinks(absoluteDirectory);
        Directory.CreateDirectory(absoluteDirectory);
        _ = Directory.EnumerateFileSystemEntries(absoluteDirectory).Take(1).ToArray();
        var temporary = Path.Combine(absoluteDirectory, ".finalcheck-probe-" + Guid.NewGuid().ToString("N"));
        var renamed = temporary + ".renamed";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.WriteByte(0x46);
                stream.Flush(true);
                stream.Position = 0;
                if (stream.ReadByte() != 0x46) throw new IOException("Directory readback failed.");
            }
            File.Move(temporary, renamed, false);
        }
        finally
        {
            // Only these exact, unique probe files can be removed; user files are never touched.
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(renamed)) File.Delete(renamed);
        }
    }
}

public sealed class DataRootValidator(IPlatformStoragePaths paths, IStorageVolumeInfoProvider volumes,
    IStorageDirectoryProbe probe) : IDataRootValidator
{
    public DataRootValidationResult Validate(DataRootValidationRequest request)
    {
        string? normalized = null;
        long required = 0;
        long available = 0;
        DataRootValidationResult Failure(string code) => new(false, normalized, code, required, available);
        try
        {
            if (string.IsNullOrWhiteSpace(request.Path) || !Path.IsPathFullyQualified(request.Path)) return Failure("AbsolutePathRequired");
            if (request.Path.StartsWith(@"\\", StringComparison.Ordinal) || request.Path.StartsWith("//", StringComparison.Ordinal)) return Failure("NetworkOrDevicePathUnsupported");
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Path));
            if (normalized == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(normalized)!)) return Failure("DiskRootUnsupported");
            if (OperatingSystem.IsWindows() && normalized.Split(Path.DirectorySeparatorChar).Skip(1)
                .Any(component => component.EndsWith('.') || component.EndsWith(' ') || component.Contains(':')))
                return Failure("UnsafePathComponent");
            if (StorageFileSafety.Overlaps(normalized, paths.InstallDirectory)) return Failure("InstallationOverlap");
            if (request.SourceRoot is not null && StorageFileSafety.Overlaps(normalized, request.SourceRoot)) return Failure("SourceOverlap");
            StorageFileSafety.RejectLinks(normalized);
            var volume = volumes.Inspect(normalized);
            available = volume.AvailableBytes;
            if (volume.Kind != StorageDriveKind.Fixed) return Failure(volume.Kind + "DiskUnsupported");
            if (StorageFileSafety.Overlaps(normalized, volume.TemporaryDirectory)) return Failure("TemporaryDirectoryUnsupported");
            if (volume.KnownSyncRoots.Any(root => StorageFileSafety.Overlaps(normalized, root))) return Failure("KnownSyncDirectoryUnsupported");
            if (request.SourceBytes < 0 || request.DatabaseBytes < 0) return Failure("InvalidSpaceEstimate");
            // Copy/staging + extra consistent DB backup + operating margin; no optimistic free > used shortcut.
            required = checked(request.SourceBytes + request.DatabaseBytes + Math.Max(64L * 1024 * 1024, request.SourceBytes / 10));
            if (available < required) return Failure("InsufficientSpace");
            if (File.Exists(normalized)) return Failure("NotADirectory");
            if (request.RequireEmpty && Directory.Exists(normalized) && Directory.EnumerateFileSystemEntries(normalized).Any()) return Failure("TargetNotEmpty");
            probe.Verify(normalized);
            return new(true, normalized, "Validated", required, available);
        }
        catch (UnauthorizedAccessException) { return Failure("PermissionDenied"); }
        catch (IOException) { return Failure("PathAccessOrLinkFailure"); }
        catch (ArgumentException) { return Failure("InvalidPath"); }
        catch (OverflowException) { return Failure("SpaceEstimateOverflow"); }
        catch (NotSupportedException) { return Failure("UnsupportedPath"); }
    }
}
