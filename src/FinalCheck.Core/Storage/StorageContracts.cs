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
