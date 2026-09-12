using System.Text.Json;
using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

public sealed class DataRootBootstrapResolver(IPlatformStoragePaths paths, IStorageBootstrapStore bootstrap,
    IDataRootDatabaseInspector inspector, IStorageVolumeInfoProvider? volumes = null)
{
    public const string IdentityFileName = ".finalcheck-root.json";
    private static readonly string[] ManagedHistoryNames = ["WorkingCopies", "Backups", "Snapshots", "Comparisons", "finalcheck.db-wal", "finalcheck.db-shm"];

    public async Task<DataRootResolution> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var existing = bootstrap.Load(); // Corrupt/unknown locator never means a new user.
        if (existing is not null)
        {
            var descriptor = existing.Current;
            var recovery = "ExistingBootstrap";
            if (!Directory.Exists(descriptor.Path))
            {
                descriptor = existing.LastKnownGood;
                recovery = "LastKnownGood";
            }
            await ValidateExistingAsync(descriptor, existing.DatabaseInitialized, cancellationToken);
            if (recovery == "LastKnownGood") bootstrap.Save(existing with { Current = descriptor }, existing.Current.Generation);
            return new(new DataRootPaths(descriptor), existing.DatabaseInitialized, recovery);
        }

        // Existing managed files without their database are damage, not a fresh installation.
        var legacyDatabase = Path.Combine(paths.LegacyDataRoot, "finalcheck.db");
        var defaultDatabase = Path.Combine(paths.DefaultDataRoot, "finalcheck.db");
        var legacyPresent = File.Exists(legacyDatabase);
        var defaultPresent = File.Exists(defaultDatabase);
        if (legacyPresent && defaultPresent) throw new InvalidDataException("Two data roots found without a locator; manual recovery is required.");
        var root = legacyPresent ? paths.LegacyDataRoot : paths.DefaultDataRoot;
        var initialized = legacyPresent || defaultPresent;
        EnsureSafe(root);
        if (initialized) await inspector.ValidateAsync(legacyPresent ? legacyDatabase : defaultDatabase, cancellationToken);
        else if (HasLegacyDataWithoutDatabase(paths.LegacyDataRoot) || Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidDataException("Data history exists without a valid database; refusing to create an empty database.");

        var identityPath = Path.Combine(root, IdentityFileName);
        var descriptorNew = File.Exists(identityPath)
            ? ReadIdentity(root)
            : new DataRootDescriptor(Path.GetFullPath(root), Guid.NewGuid(), DataRootPaths.CurrentLayoutVersion, 1);
        Directory.CreateDirectory(root);
        if (!File.Exists(identityPath)) WriteIdentity(descriptorNew);
        var value = new StorageBootstrap(StorageBootstrap.CurrentSchemaVersion, descriptorNew, descriptorNew, initialized);
        bootstrap.Save(value, null);
        return new(new DataRootPaths(descriptorNew), initialized, initialized ? "AdoptedExistingDatabase" : "NewUser");
    }

    public async Task MarkDatabaseInitializedAsync(CancellationToken cancellationToken = default)
    {
        var current = bootstrap.Load() ?? throw new InvalidDataException("Missing bootstrap.");
        await inspector.ValidateAsync(new DataRootPaths(current.Current).DatabasePath, cancellationToken);
        bootstrap.Save(current with { DatabaseInitialized = true }, current.Current.Generation);
    }

    private async Task ValidateExistingAsync(DataRootDescriptor descriptor, bool initialized, CancellationToken token)
    {
        EnsureSafe(descriptor.Path);
        if (!Directory.Exists(descriptor.Path)) throw new DirectoryNotFoundException("Configured data root is unavailable; no new database will be created.");
        var actual = ReadIdentity(descriptor.Path);
        if (actual.RootId != descriptor.RootId || actual.Generation != descriptor.Generation || actual.LayoutVersion != descriptor.LayoutVersion)
            throw new InvalidDataException("Data root identity/generation mismatch.");
        var database = new DataRootPaths(descriptor).DatabasePath;
        if (File.Exists(database)) await inspector.ValidateAsync(database, token);
        else if (initialized || HasManagedHistory(descriptor.Path) || Directory.EnumerateFileSystemEntries(descriptor.Path)
            .Any(entry => Path.GetFileName(entry) != IdentityFileName))
            throw new InvalidDataException("Configured database is missing; no empty replacement is allowed.");
    }

    private void EnsureSafe(string root)
    {
        if (!Path.IsPathFullyQualified(root) || StorageFileSafety.Overlaps(root, paths.InstallDirectory))
            throw new InvalidDataException("Data root must be absolute and independent of installation files.");
        StorageFileSafety.RejectLinks(root);
        if (root.StartsWith("\\\\", StringComparison.Ordinal) || root.StartsWith("//", StringComparison.Ordinal) ||
            Path.TrimEndingDirectorySeparator(root) == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!))
            throw new InvalidDataException("A network path or disk root cannot host application data.");
        if (volumes is not null)
        {
            var facts = volumes.Inspect(root);
            if (facts.Kind != StorageDriveKind.Fixed || StorageFileSafety.Overlaps(root, facts.TemporaryDirectory) ||
                facts.KnownSyncRoots.Any(sync => StorageFileSafety.Overlaps(root, sync)))
                throw new InvalidDataException("Configured data root does not satisfy the fixed-local storage policy.");
        }
    }

    private static bool HasManagedHistory(string root) =>
        ManagedHistoryNames
            .Any(name => File.Exists(Path.Combine(root, name)) || Directory.Exists(Path.Combine(root, name)));

    private static bool HasLegacyDataWithoutDatabase(string root) => Directory.Exists(root) &&
        Directory.EnumerateFileSystemEntries(root).Any(entry =>
            Path.GetFileName(entry) != "Data" && Path.GetFileName(entry) != "bootstrap.lock" &&
            Path.GetFileName(entry) != "startup-diagnostic.log" &&
            !Path.GetFileName(entry).StartsWith("bootstrap.json.", StringComparison.Ordinal));

    public static void WriteIdentity(DataRootDescriptor descriptor)
        => WriteIdentityAt(descriptor.Path, descriptor);

    public static void WriteIdentityAt(string physicalRoot, DataRootDescriptor descriptor)
    {
        StorageFileSafety.RejectLinks(physicalRoot);
        Directory.CreateDirectory(physicalRoot);
        using var output = new FileStream(Path.Combine(physicalRoot, IdentityFileName), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        JsonSerializer.Serialize(output, descriptor);
        output.Flush(true);
    }

    private static DataRootDescriptor ReadIdentity(string root)
    {
        StorageFileSafety.RejectLinks(Path.Combine(root, IdentityFileName));
        try
        {
            var identity = JsonSerializer.Deserialize<DataRootDescriptor>(File.ReadAllBytes(Path.Combine(root, IdentityFileName)))
                ?? throw new InvalidDataException("Missing data root identity.");
            _ = new DataRootPaths(identity);
            return identity;
        }
        catch (JsonException error) { throw new InvalidDataException("Corrupt data root identity.", error); }
    }
}
