using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Storage;
using FinalCheck.Data;
using FinalCheck.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;

/// <summary>Storage composition shared by the real Desktop entry and isolated startup/packaged-payload checks.</summary>
public static class DesktopStorageServices
{
    public static async Task ConfigureAsync(IServiceCollection services, IPlatformStoragePaths platform,
        IStorageVolumeInfoProvider? volumes = null, CancellationToken cancellationToken = default)
    {
        var bootstrap = new FileStorageBootstrapStore(platform);
        var inspector = new SqliteDataRootDatabaseInspector();
        volumes ??= new PlatformStorageVolumeInfoProvider();
        var resolver = new DataRootBootstrapResolver(platform, bootstrap, inspector, volumes);
        var resolved = await resolver.ResolveAsync(cancellationToken); // No context/services before root recognition.
        services.AddSingleton(platform);
        services.AddSingleton<IStorageBootstrapStore>(bootstrap);
        services.AddSingleton<IDataRootDatabaseInspector>(inspector);
        services.AddSingleton(resolver);
        services.AddSingleton(sp => new StorageMaintenanceCoordinator(resolved.Provider, platform, bootstrap, resolver));
        services.AddSingleton<IDataRootProvider>(sp => sp.GetRequiredService<StorageMaintenanceCoordinator>());
        services.AddSingleton<IStorageMaintenanceCoordinator>(sp => sp.GetRequiredService<StorageMaintenanceCoordinator>());
        services.AddSingleton<IStorageRootChangeNotifier>(sp => sp.GetRequiredService<StorageMaintenanceCoordinator>());
        services.AddSingleton(volumes);
        services.AddSingleton<IStorageDirectoryProbe, StorageDirectoryProbe>();
        services.AddSingleton<IDataRootValidator, DataRootValidator>();
        services.AddSingleton<StorageMigrationJournal>();
        services.AddSingleton<IStorageDatabaseMigrationService, SqliteStorageDatabaseMigrationService>();
        services.AddSingleton<IDataRootMigrationService, DataRootMigrationService>();
        services.AddSingleton<StorageMigrationRecoveryService>();
        services.AddSingleton<IStorageDatabaseUsageReader, SqliteStorageUsageReader>();
        services.AddSingleton<IStorageUsageService, StorageUsageService>();
        services.AddSingleton<DataRootDbContextFactory>();
        services.AddScoped(sp => sp.GetRequiredService<DataRootDbContextFactory>().CreateDbContext());
        services.AddScoped<FinalCheckDatabaseInitializer>();
        services.AddScoped<IAppDataPathProvider>(sp => new PlatformAppDataPathProvider(sp.GetRequiredService<FinalCheckDbContext>().ManagedPaths));
    }

    public static async Task<IReadOnlyList<StorageRecoveryDiagnostic>> InitializeAsync(IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var recovery = await services.GetRequiredService<StorageMigrationRecoveryService>().RecoverAsync(cancellationToken);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FinalCheckDatabaseInitializer>().InitializeAsync(cancellationToken);
        await services.GetRequiredService<DataRootBootstrapResolver>().MarkDatabaseInitializedAsync(cancellationToken);
        return recovery;
    }
}
