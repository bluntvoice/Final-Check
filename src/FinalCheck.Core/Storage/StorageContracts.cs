namespace FinalCheck.Core.Storage;

public sealed record DataRootDescriptor(string Path, Guid RootId, int LayoutVersion, long Generation);

/// <summary>Immutable paths for one storage generation; no operating-system discovery.</summary>
public interface IDataRootProvider
{
    DataRootDescriptor Descriptor { get; }
    string CurrentDataRoot { get; }
    string DatabasePath { get; }
    string SnapshotPath { get; }
    string WorkingCopyPath { get; }
    string BackupPath { get; }
    string CachePath { get; }
    string LogPath { get; }
    string TempPath { get; }
}

public sealed class DataRootPaths : IDataRootProvider
{
    public const int CurrentLayoutVersion = 1;
    public DataRootPaths(DataRootDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Path) || !System.IO.Path.IsPathFullyQualified(descriptor.Path) ||
            descriptor.RootId == Guid.Empty || descriptor.LayoutVersion != CurrentLayoutVersion || descriptor.Generation < 1)
            throw new InvalidDataException("Invalid or unsupported data root descriptor.");
        Descriptor = descriptor with { Path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(descriptor.Path)) };
    }
    public DataRootDescriptor Descriptor { get; }
    public string CurrentDataRoot => Descriptor.Path;
    public string DatabasePath => System.IO.Path.Combine(CurrentDataRoot, "finalcheck.db");
    // Snapshot, Comparison and restore payloads currently reside inside DatabasePath.
    public string SnapshotPath => DatabasePath;
    public string WorkingCopyPath => System.IO.Path.Combine(CurrentDataRoot, "WorkingCopies");
    public string BackupPath => System.IO.Path.Combine(CurrentDataRoot, "Backups");
    public string CachePath => System.IO.Path.Combine(CurrentDataRoot, "Cache");
    public string LogPath => System.IO.Path.Combine(CurrentDataRoot, "Logs");
    public string TempPath => System.IO.Path.Combine(CurrentDataRoot, "Temp");
}

public interface IPlatformStoragePaths
{
    string ConfigurationDirectory { get; }
    string DefaultDataRoot { get; }
    string LegacyDataRoot { get; }
    string InstallDirectory { get; }
}

public sealed record StorageBootstrap(int SchemaVersion, DataRootDescriptor Current,
    DataRootDescriptor LastKnownGood, bool DatabaseInitialized)
{
    public const int CurrentSchemaVersion = 1;
}

public interface IStorageBootstrapStore
{
    StorageBootstrap? Load();
    void Save(StorageBootstrap bootstrap, long? expectedGeneration);
}

public interface IDataRootDatabaseInspector
{
    Task ValidateAsync(string databasePath, CancellationToken cancellationToken = default);
}

public sealed record DataRootResolution(IDataRootProvider Provider, bool DatabaseInitialized,
    string RecoveryCode);

public enum StorageDriveKind { Fixed, Network, Removable, Unknown }
public sealed record StorageVolumeInfo(StorageDriveKind Kind, long AvailableBytes,
    string TemporaryDirectory, IReadOnlyList<string> KnownSyncRoots);
public interface IStorageVolumeInfoProvider
{
    StorageVolumeInfo Inspect(string absolutePath);
}
public sealed record DataRootValidationRequest(string Path, string? SourceRoot = null,
    long SourceBytes = 0, long DatabaseBytes = 0, bool RequireEmpty = true);
public sealed record DataRootValidationResult(bool IsValid, string? NormalizedPath, string Code,
    long RequiredBytes, long AvailableBytes);
public interface IDataRootValidator
{
    DataRootValidationResult Validate(DataRootValidationRequest request);
}

public interface IStorageDataSession : IDataRootProvider, IDisposable
{
    void EnsureActive();
}
public interface IStorageMaintenanceLease : IDisposable
{
    void StageRoot(DataRootDescriptor descriptor);
    void Complete();
    void RequireRecovery();
}
public interface IStorageMaintenanceCoordinator
{
    IStorageDataSession OpenSession();
    Task<IStorageMaintenanceLease> EnterMaintenanceAsync(CancellationToken cancellationToken = default);
}

public enum StorageMigrationStage { Validating, Quiescing, BackingUp, Copying, ValidatingCopy, Relocating, Finalizing, PreparingRuntime, Committing, Completed }
public enum StorageMigrationStatus { InProgress, Completed, Failed, Cancelled, NeedsReview }
public sealed record StorageFileVerification(string RelativePath, long Size, string Sha256);
public sealed record StorageMigrationRecord(int SchemaVersion, Guid MigrationId, DataRootDescriptor Source,
    DataRootDescriptor Target, string StagingPath, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    StorageMigrationStatus Status, StorageMigrationStage Stage, string? FailureCode, long Size,
    IReadOnlyList<StorageFileVerification> Files, bool DataValidated);
public sealed record StorageMigrationResult(StorageMigrationStatus Status, StorageMigrationStage Stage,
    Guid MigrationId, string? Code, string SourceRoot, string TargetRoot);
public interface IDataRootMigrationService
{
    Task<StorageMigrationResult> MigrateAsync(string target, CancellationToken cancellationToken = default);
}
public interface IStorageDatabaseMigrationService
{
    Task<IStorageDatabaseMigrationSession> OpenSourceAsync(string sourceRoot, CancellationToken cancellationToken = default);
}
public interface IStorageDatabaseMigrationSession : IAsyncDisposable
{
    IReadOnlySet<string> OriginalPaths { get; }
    Task BackupAsync(string stagingRoot, Guid operationId, CancellationToken cancellationToken = default);
    Task RelocateAndValidateAsync(string physicalRoot, string logicalRoot, CancellationToken cancellationToken = default);
    Task ValidateReopenedAsync(string targetRoot, CancellationToken cancellationToken = default);
}

public interface IStorageRootChangeNotifier
{
    event Action<DataRootDescriptor>? DataRootChanged;
}
public sealed record StorageDatabaseUsage(long SnapshotBytes, long ComparisonBytes, long RestoreBytes,
    IReadOnlySet<string> OriginalPaths);
public interface IStorageDatabaseUsageReader
{
    // Caller holds the generation session for this database during the read and file scan.
    Task<StorageDatabaseUsage> ReadAsync(string databasePath, CancellationToken cancellationToken = default);
}
public sealed record StorageUsage(DataRootDescriptor Root, DateTimeOffset SampledAt, long DatabaseBytes,
    long SnapshotBytes, long ComparisonBytes, long RestoreBytes, long WorkingCopyBytes, long BackupBytes,
    long CacheBytes, long LogBytes, long TempBytes, long OtherBytes, long TotalBytes,
    bool IsComplete, IReadOnlyList<string> Diagnostics)
{
    public bool PayloadBytesIncludedInDatabase { get; init; } = true;
}
public interface IStorageUsageService
{
    Task<StorageUsage> CalculateAsync(CancellationToken cancellationToken = default);
}
