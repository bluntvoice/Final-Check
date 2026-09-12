using System.Text.Json;
using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

/// <summary>Small locator only. Exclusive lock and same-volume atomic publication.</summary>
public sealed class FileStorageBootstrapStore(IPlatformStoragePaths paths) : IStorageBootstrapStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string BootstrapPath => Path.Combine(paths.ConfigurationDirectory, "bootstrap.json");

    public StorageBootstrap? Load()
    {
        StorageFileSafety.RejectLinks(BootstrapPath);
        if (!File.Exists(BootstrapPath)) return null;
        try
        {
            var value = JsonSerializer.Deserialize<StorageBootstrap>(File.ReadAllBytes(BootstrapPath), Options)
                ?? throw new InvalidDataException("Empty storage bootstrap.");
            Validate(value);
            return value;
        }
        catch (JsonException error) { throw new InvalidDataException("Storage bootstrap is corrupt; refusing to create an empty database.", error); }
    }

    public void Save(StorageBootstrap bootstrap, long? expectedGeneration)
    {
        Validate(bootstrap);
        StorageFileSafety.RejectLinks(paths.ConfigurationDirectory);
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        using var gate = new FileStream(Path.Combine(paths.ConfigurationDirectory, "bootstrap.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = Load();
        if (previous?.Current.Generation != expectedGeneration)
            throw new InvalidOperationException("Storage bootstrap generation changed.");
        if (previous is not null && bootstrap.Current.Generation < previous.Current.Generation)
            throw new InvalidOperationException("Storage bootstrap cannot move backwards.");
        if (previous is not null && bootstrap.Current.Generation == previous.Current.Generation &&
            (bootstrap.Current != previous.Current && bootstrap.Current != previous.LastKnownGood ||
             previous.DatabaseInitialized && !bootstrap.DatabaseInitialized))
            throw new InvalidOperationException("Changing storage identity requires a new generation; initialized databases cannot be forgotten.");
        var temporary = BootstrapPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(bootstrap, Options);
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            output.Write(bytes);
            output.Flush(true);
        }
        var validated = JsonSerializer.Deserialize<StorageBootstrap>(File.ReadAllBytes(temporary), Options)!;
        Validate(validated);
        if (validated != bootstrap) throw new InvalidDataException("Bootstrap verification failed.");
        if (previous is null) File.Move(temporary, BootstrapPath, false);
        else File.Replace(temporary, BootstrapPath, BootstrapPath + ".previous", true);
        if (Load() != bootstrap) throw new IOException("Bootstrap publication could not be confirmed; recovery is required.");
        // An old previous locator is audit/recovery material, never an automatic stale-data fallback.
    }

    private static void Validate(StorageBootstrap value)
    {
        if (value.SchemaVersion != StorageBootstrap.CurrentSchemaVersion || value.Current is null || value.LastKnownGood is null)
            throw new InvalidDataException("Unsupported storage bootstrap schema.");
        _ = new DataRootPaths(value.Current);
        _ = new DataRootPaths(value.LastKnownGood);
        if (value.Current.RootId != value.LastKnownGood.RootId || value.Current.Generation != value.LastKnownGood.Generation)
            throw new InvalidDataException("LastKnownGood must identify the current committed generation, not a stale migration source.");
    }
}

internal static class StorageFileSafety
{
    public static void RejectLinks(string path)
    {
        for (var cursor = Path.GetFullPath(path); !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
            if ((Directory.Exists(cursor) || File.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked/reparse-point storage paths are unsupported.");
    }
    public static bool Overlaps(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return a.Equals(b, comparison) || a.StartsWith(b + Path.DirectorySeparatorChar, comparison) || b.StartsWith(a + Path.DirectorySeparatorChar, comparison);
    }
}
