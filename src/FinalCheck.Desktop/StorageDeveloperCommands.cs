#if DEBUG
using System.Text.Json;
using FinalCheck.Core.Storage;
using FinalCheck.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;

internal sealed class DeveloperStorageVolumeInfo(DeveloperStoragePaths paths) : IStorageVolumeInfoProvider
{
    public StorageVolumeInfo Inspect(string absolutePath)
    {
        var allowed = paths.DefaultDataRoot + ".target";
        if (Path.GetFullPath(absolutePath) != allowed && Path.GetFullPath(absolutePath) != paths.DefaultDataRoot)
            throw new IOException("Developer migration target must be the isolated data directory plus .target.");
        var actual = new PlatformStorageVolumeInfoProvider().Inspect(absolutePath);
        // Narrow test-only allowance: a known isolated sibling, never arbitrary production Temp locations.
        return actual with { TemporaryDirectory = paths.DefaultDataRoot + ".system-temp" };
    }
}

internal static class StorageDeveloperCommands
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static async Task<bool> RunAsync(string[] args, IPlatformStoragePaths paths, IServiceProvider services)
    {
        var validate = Array.IndexOf(args, "--storage-validate");
        var migrate = Array.IndexOf(args, "--storage-migrate");
        if (!args.Contains("--storage-info", StringComparer.Ordinal) && validate < 0 && migrate < 0) return false;
        if (paths is not DeveloperStoragePaths) throw new ArgumentException("Storage developer commands require explicit isolated developer data.");
        if (validate >= 0 && migrate >= 0) throw new ArgumentException("Choose validate or migrate, not both.");
        string Value(int index) => index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("A target absolute directory is required.");
        if (validate >= 0)
        {
            var usage = await services.GetRequiredService<IStorageUsageService>().CalculateAsync();
            if (!usage.IsComplete) throw new InvalidDataException("Cannot validate migration budget from partial storage usage.");
            Console.WriteLine(JsonSerializer.Serialize(services.GetRequiredService<IDataRootValidator>().Validate(new(Value(validate), usage.Root.Path, usage.TotalBytes, usage.DatabaseBytes)), Options));
        }
        if (migrate >= 0)
            Console.WriteLine(JsonSerializer.Serialize(await services.GetRequiredService<IDataRootMigrationService>().MigrateAsync(Value(migrate)), Options));
        var provider = services.GetRequiredService<IDataRootProvider>();
        Console.WriteLine(JsonSerializer.Serialize(new { provider.Descriptor, provider.DatabasePath, provider.SnapshotPath,
            provider.WorkingCopyPath, provider.BackupPath, provider.CachePath, provider.LogPath, provider.TempPath,
            Usage = await services.GetRequiredService<IStorageUsageService>().CalculateAsync() }, Options));
        return true;
    }
}
#endif
