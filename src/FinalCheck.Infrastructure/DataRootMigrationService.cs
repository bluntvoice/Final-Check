using System.Security.Cryptography;
using System.Text.Json;
using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

public sealed class StorageMigrationJournal(IPlatformStoragePaths platform)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public void Save(StorageMigrationRecord record)
    {
        var location = Path.Combine(platform.ConfigurationDirectory, $"migration-{record.MigrationId:N}.json");
        StorageFileSafety.RejectLinks(location);
        Directory.CreateDirectory(platform.ConfigurationDirectory);
        var temporary = location + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, record, Options);
            stream.Flush(true);
        }
        if (JsonSerializer.Deserialize<StorageMigrationRecord>(File.ReadAllBytes(temporary), Options) is not { SchemaVersion: 1 } verified || verified.MigrationId != record.MigrationId)
            throw new InvalidDataException("Migration journal readback failed.");
        File.Move(temporary, location, true); // Only this operation's versioned journal, same-volume atomic rename.
    }
    public IReadOnlyList<StorageMigrationRecord> Load()
    {
        if (!Directory.Exists(platform.ConfigurationDirectory)) return [];
        return Directory.EnumerateFiles(platform.ConfigurationDirectory, "migration-*.json").Select(location =>
        {
            StorageFileSafety.RejectLinks(location);
            var record = JsonSerializer.Deserialize<StorageMigrationRecord>(File.ReadAllBytes(location), Options);
            if (record is not { SchemaVersion: 1 } || Path.GetFileName(location) != $"migration-{record.MigrationId:N}.json")
                throw new InvalidDataException("Unknown migration journal.");
            return record;
        }).ToArray();
    }
}

public interface IStorageMigrationFileCopier
{
    Task CopyAsync(string source, string target, CancellationToken cancellationToken);
}
public sealed class StorageMigrationFileCopier : IStorageMigrationFileCopier
{
    public async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        StorageFileSafety.RejectLinks(source);
        StorageFileSafety.RejectLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough | FileOptions.Asynchronous);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken); output.Flush(true);
    }
}

public sealed class DataRootMigrationService(IDataRootProvider provider, IPlatformStoragePaths platform,
    IDataRootValidator validator, IStorageMaintenanceCoordinator coordinator, IStorageBootstrapStore bootstrap,
    IStorageDatabaseMigrationService databases, StorageMigrationJournal journal,
    IStorageMigrationFileCopier? fileCopier = null) : IDataRootMigrationService
{
    private static readonly string[] ManagedDirectories = ["WorkingCopies", "Backups", "Cache", "Logs", "Temp", "Settings", "Snapshots", "Comparisons"];
    public async Task<StorageMigrationResult> MigrateAsync(string target, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var source = provider.Descriptor;
        var stage = StorageMigrationStage.Validating;
        StorageMigrationRecord? record = null;
        IStorageMaintenanceLease? maintenance = null;
        var committed = false;
        StorageMigrationResult Result(StorageMigrationStatus status, string? code) => new(status, stage, id, code, source.Path, target);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = SafeFiles(source.Path).Sum(path => new FileInfo(path).Length);
            var databaseBytes = new FileInfo(provider.DatabasePath).Length;
            var validation = validator.Validate(new(target, source.Path, size, databaseBytes));
            if (!validation.IsValid) return Result(StorageMigrationStatus.Failed, validation.Code);
            target = validation.NormalizedPath!;
            var descriptor = new DataRootDescriptor(target, Guid.NewGuid(), source.LayoutVersion, checked(source.Generation + 1));
            var staging = target + $".finalcheck-migration-{id:N}";
            record = new(1, id, source, descriptor, staging, DateTimeOffset.UtcNow, null, StorageMigrationStatus.InProgress, stage, null, size, [], false);
            journal.Save(record);
            void Advance(StorageMigrationStage next)
            {
                stage = next;
                record = record! with { Stage = next };
                journal.Save(record);
                cancellationToken.ThrowIfCancellationRequested();
            }
            Advance(StorageMigrationStage.Quiescing);
            maintenance = await coordinator.EnterMaintenanceAsync(cancellationToken);
            if (provider.Descriptor != source) throw new InvalidDataException("Source storage generation changed before maintenance.");
            // Revalidate after draining/locking; preflight space and directory state can become stale.
            size = SafeFiles(source.Path).Sum(path => new FileInfo(path).Length);
            databaseBytes = new FileInfo(provider.DatabasePath).Length;
            record = record with { Size = size };
            validation = validator.Validate(new(target, source.Path, size, databaseBytes));
            if (!validation.IsValid) throw new InvalidDataException(validation.Code);
            Advance(StorageMigrationStage.BackingUp);
            if (Directory.Exists(staging)) throw new IOException("Unique staging directory already exists.");
            Directory.CreateDirectory(staging);
            await using var database = await databases.OpenSourceAsync(source.Path, cancellationToken);
            var inventory = await InventoryAsync(source.Path, database.OriginalPaths, cancellationToken);
            using var sourceFiles = ProtectFiles(source.Path, inventory.Where(item => item.RelativePath.StartsWith("WorkingCopies" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                item.RelativePath.StartsWith("Backups" + Path.DirectorySeparatorChar, StringComparison.Ordinal)).Select(item => item.RelativePath)
                .Append(DataRootBootstrapResolver.IdentityFileName));
            await database.BackupAsync(staging, id, cancellationToken);
            Advance(StorageMigrationStage.Copying);
            foreach (var item in inventory)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var original = Path.Combine(source.Path, item.RelativePath);
                var destination = Path.Combine(staging, item.RelativePath);
                await (fileCopier ?? new StorageMigrationFileCopier()).CopyAsync(original, destination, cancellationToken);
            }
            Advance(StorageMigrationStage.ValidatingCopy);
            await VerifyAsync(staging, inventory, cancellationToken);
            await VerifyAsync(source.Path, inventory, cancellationToken);
            Advance(StorageMigrationStage.Relocating);
            await database.RelocateAndValidateAsync(staging, target, cancellationToken);
            DataRootBootstrapResolver.WriteIdentityAt(staging, descriptor);
            record = record with { Files = inventory };
            Advance(StorageMigrationStage.Finalizing);
            if (Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("Target became nonempty before finalize.");
            // Preserve even the selected empty directory, then atomically publish staging on the target volume.
            Directory.Move(target, target + $".empty-{id:N}");
            Directory.Move(staging, target);
            Advance(StorageMigrationStage.PreparingRuntime);
            await database.ValidateReopenedAsync(target, cancellationToken);
            await VerifyAsync(target, inventory, cancellationToken);
            await VerifyAsync(source.Path, inventory, cancellationToken);
            if (!(await InventoryAsync(source.Path, database.OriginalPaths, cancellationToken)).SequenceEqual(inventory))
                throw new InvalidDataException("Source managed file inventory changed during migration.");
            var completeManifest = new List<StorageFileVerification>();
            foreach (var file in SafeFiles(target).Where(file => Path.GetFileName(file) is not "finalcheck.db-wal" and not "finalcheck.db-shm"))
                completeManifest.Add(new(Path.GetRelativePath(target, file), new FileInfo(file).Length, await HashAsync(file, cancellationToken)));
            var expectedFiles = inventory.Select(item => item.RelativePath).Append("finalcheck.db")
                .Append(DataRootBootstrapResolver.IdentityFileName)
                .Append(Path.Combine("Backups", $"storage-migration-{id:N}", "source-finalcheck.db"))
                .ToHashSet(StringComparer.Ordinal);
            if (!expectedFiles.SetEquals(completeManifest.Select(item => item.RelativePath)))
                throw new InvalidDataException("Target file inventory contains unexpected or missing files.");
            record = record with { Files = completeManifest, DataValidated = true };
            using var targetFiles = ProtectFiles(target, completeManifest.Select(item => item.RelativePath));
            // No fallible initialization or user writes are allowed after this preparation.
            maintenance.StageRoot(descriptor);
            Advance(StorageMigrationStage.Committing);
            var previous = bootstrap.Load() ?? throw new InvalidDataException("Missing locator during commit.");
            if (previous.Current != source) throw new InvalidDataException("Locator changed before commit.");
            // Cancellation stops here. Ambiguous publication is investigated, never blindly rolled back/retried.
            try { bootstrap.Save(new(1, descriptor, descriptor, true), source.Generation); }
            catch
            {
                var durable = bootstrap.Load();
                if (durable?.Current == descriptor) committed = true;
                else if (durable?.Current != source) { maintenance.RequireRecovery(); throw; }
                if (!committed) throw;
            }
            committed = true;
            maintenance.Complete();
            stage = StorageMigrationStage.Completed;
            record = record with { Stage = stage, Status = StorageMigrationStatus.Completed, CompletedAt = DateTimeOffset.UtcNow };
            try { journal.Save(record); } catch (IOException) { /* Durable new locator is authoritative; startup finishes this receipt. */ }
            return Result(StorageMigrationStatus.Completed, "OldDataPreserved");
        }
        catch (Exception error)
        {
            if (committed) return Result(StorageMigrationStatus.Completed, "CommittedReceiptNeedsRecovery");
            var status = error is OperationCanceledException ? StorageMigrationStatus.Cancelled : StorageMigrationStatus.Failed;
            if (stage == StorageMigrationStage.Committing)
            {
                try
                {
                    if (bootstrap.Load()?.Current != source) { status = StorageMigrationStatus.NeedsReview; maintenance?.RequireRecovery(); }
                }
                catch { status = StorageMigrationStatus.NeedsReview; maintenance?.RequireRecovery(); }
            }
            if (record is not null)
            {
                try { journal.Save(record with { Status = status, FailureCode = error.GetType().Name, CompletedAt = DateTimeOffset.UtcNow }); }
                catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException) { }
            }
            return Result(status, error is InvalidDataException ? error.Message : error.GetType().Name);
        }
        finally { maintenance?.Dispose(); }
    }

    private async Task<IReadOnlyList<StorageFileVerification>> InventoryAsync(string root, IReadOnlySet<string> originals, CancellationToken token)
    {
        var files = new List<StorageFileVerification>();
        var knownJournals = root == platform.ConfigurationDirectory ? journal.Load().Select(record => $"migration-{record.MigrationId:N}.json").ToHashSet(StringComparer.Ordinal) : [];
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            StorageFileSafety.RejectLinks(entry);
            var name = Path.GetFileName(entry);
            if (originals.Contains(entry) || name is "finalcheck.db" or "finalcheck.db-wal" or "finalcheck.db-shm" or DataRootBootstrapResolver.IdentityFileName) continue;
            if (root == platform.ConfigurationDirectory && (IsControl(name) || knownJournals.Contains(name))) continue;
            if (!Directory.Exists(entry) || !ManagedDirectories.Contains(name, StringComparer.Ordinal))
                throw new InvalidDataException("Unrecognized data-root entry; refusing implicit copy/drop.");
            foreach (var file in SafeFiles(entry))
            {
                StorageFileSafety.RejectLinks(file);
                if (originals.Contains(file)) continue;
                var relative = Path.GetRelativePath(root, file);
                files.Add(new(relative, new FileInfo(file).Length, await HashAsync(file, token)));
            }
        }
        return files.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray();
    }
    private static bool IsControl(string name) => name is "bootstrap.json" or "bootstrap.json.previous" or "bootstrap.lock" or "storage-session.lock" or "startup-diagnostic.log";
    private static ProtectedFiles ProtectFiles(string root, IEnumerable<string> relatives)
    {
        var streams = new List<FileStream>();
        try
        {
            foreach (var relative in relatives)
                streams.Add(new FileStream(Path.Combine(root, relative), FileMode.Open, FileAccess.Read, FileShare.Read));
            return new ProtectedFiles(streams);
        }
        catch { foreach (var stream in streams) stream.Dispose(); throw; }
    }
    private sealed class ProtectedFiles(List<FileStream> streams) : IDisposable
    {
        public void Dispose() { foreach (var stream in streams) stream.Dispose(); }
    }
    private static IEnumerable<string> SafeFiles(string root)
    {
        StorageFileSafety.RejectLinks(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            StorageFileSafety.RejectLinks(entry);
            if (Directory.Exists(entry)) { foreach (var file in SafeFiles(entry)) yield return file; }
            else yield return entry;
        }
    }
    private static async Task VerifyAsync(string root, IReadOnlyList<StorageFileVerification> files, CancellationToken token)
    {
        foreach (var item in files)
        {
            var path = Path.Combine(root, item.RelativePath);
            StorageFileSafety.RejectLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length != item.Size || await HashAsync(path, token) != item.Sha256)
                throw new InvalidDataException("Managed file count/size/hash validation failed.");
        }
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
}
