using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

public sealed class StorageUsageService(IStorageMaintenanceCoordinator coordinator, IPlatformStoragePaths platform,
    IStorageDatabaseUsageReader database, StorageMigrationJournal journal) : IStorageUsageService
{
    public async Task<StorageUsage> CalculateAsync(CancellationToken cancellationToken = default)
    {
        using var session = coordinator.OpenSession();
        var diagnostics = new List<string>();
        StorageDatabaseUsage logical = new(0, 0, 0, new HashSet<string>());
        try { logical = await database.ReadAsync(session.DatabasePath, cancellationToken); }
        catch (Exception error) when (error is not OperationCanceledException)
        { diagnostics.Add("DatabaseUsage:" + error.GetType().Name); }
        var counts = new Dictionary<string, long>(StringComparer.Ordinal)
        { ["Database"] = 0, ["WorkingCopy"] = 0, ["Backup"] = 0, ["Cache"] = 0, ["Logs"] = 0, ["Temp"] = 0, ["Other"] = 0 };
        var control = session.CurrentDataRoot == platform.ConfigurationDirectory
            ? journal.Load().Select(record => $"migration-{record.MigrationId:N}.json").ToHashSet(StringComparer.Ordinal) : [];
        void Scan(string directory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StorageFileSafety.RejectLinks(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    StorageFileSafety.RejectLinks(path);
                    if (logical.OriginalPaths.Contains(path)) continue;
                    if (Directory.Exists(path)) { Scan(path); continue; }
                    var relative = Path.GetRelativePath(session.CurrentDataRoot, path);
                    if (session.CurrentDataRoot == platform.ConfigurationDirectory &&
                        (control.Contains(relative) || relative is "bootstrap.json" or "bootstrap.json.previous" or "bootstrap.lock" or "storage-session.lock" or "startup-diagnostic.log")) continue;
                    var category = Category(relative);
                    counts[category] = checked(counts[category] + new FileInfo(path).Length);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or OverflowException)
                { diagnostics.Add("FileUsage:" + error.GetType().Name); }
            }
        }
        try { Scan(session.CurrentDataRoot); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { diagnostics.Add("RootUsage:" + error.GetType().Name); }
        return new(session.Descriptor, DateTimeOffset.UtcNow, counts["Database"], logical.SnapshotBytes, logical.ComparisonBytes,
            logical.RestoreBytes, counts["WorkingCopy"], counts["Backup"], counts["Cache"], counts["Logs"], counts["Temp"],
            counts["Other"], counts.Values.Sum(), diagnostics.Count == 0, diagnostics);
    }
    private static string Category(string relative)
    {
        if (relative is "finalcheck.db" or "finalcheck.db-wal" or "finalcheck.db-shm") return "Database";
        var parts = relative.Split(Path.DirectorySeparatorChar);
        if (parts.Length > 1)
        {
            if (parts[0] is "Backups") return "Backup";
            if (parts[0] is "Cache" or "Logs" or "Temp") return parts[0];
            if (parts[0] == "WorkingCopies")
            {
                var leaf = parts[^1];
                if (leaf == "restored.docx") return "WorkingCopy";
                if (leaf.StartsWith("previous-", StringComparison.Ordinal)) return "Backup";
                if (leaf.StartsWith("candidate-", StringComparison.Ordinal)) return "Temp";
            }
        }
        return "Other";
    }
}
