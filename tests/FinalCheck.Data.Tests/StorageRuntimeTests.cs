using FinalCheck.Core.Abstractions;
using FinalCheck.Comparison;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Storage;
using FinalCheck.Desktop;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Data.Tests;

public sealed class StorageRuntimeTests
{
    private static readonly string[] DatabaseFiles = ["finalcheck.db", "finalcheck.db-wal", "finalcheck.db-shm"];
    [Fact]
    public async Task FreshStartupResolvesBeforeCreatingAnEmptyDatabase()
    {
        using var fixture = new StorageFixture();
        await using var runtime = await RuntimeAsync(fixture);
        var root = runtime.GetRequiredService<IDataRootProvider>();
        Assert.False(File.Exists(root.DatabasePath));
        Assert.False(fixture.Bootstrap.Load()!.DatabaseInitialized);
        Assert.Empty(await DesktopStorageServices.InitializeAsync(runtime));
        Assert.True(fixture.Bootstrap.Load()!.DatabaseInitialized);
        await using var scope = runtime.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FinalCheckDbContext>();
        Assert.Equal(fixture.Paths.DefaultDataRoot, context.ManagedPaths.CurrentDataRoot);
        Assert.Equal(0, await context.DocumentSnapshots.CountAsync());
    }

    [Fact]
    public async Task ExistingUserWithoutBootstrapKeepsLegacyDatabaseAndAllHistories()
    {
        await using var env = await MigrationEnvironment.CreateAsync(legacy: true);
        File.Delete(Path.Combine(env.Fixture.Paths.ConfigurationDirectory, "bootstrap.json"));
        File.Delete(Path.Combine(env.Fixture.Paths.ConfigurationDirectory, "bootstrap.json.previous"));
        await using var runtime = await RuntimeAsync(env.Fixture);
        await DesktopStorageServices.InitializeAsync(runtime);
        Assert.Equal(env.Fixture.Paths.LegacyDataRoot, runtime.GetRequiredService<IDataRootProvider>().CurrentDataRoot);
        Assert.False(File.Exists(Path.Combine(env.Fixture.Paths.DefaultDataRoot, "finalcheck.db")));
        await using (var scope = runtime.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FinalCheckDbContext>();
            Assert.Equal(2, await context.DocumentSnapshots.CountAsync());
            Assert.Equal(1, await context.ComparisonResults.CountAsync());
            Assert.Equal(2, await context.FormatRestoreOperations.CountAsync());
            Assert.Equal(1, await context.RestoredWorkingCopies.CountAsync());
        }
        var usage = await runtime.GetRequiredService<IStorageUsageService>().CalculateAsync();
        Assert.True(usage.IsComplete);
        var control = Directory.EnumerateFiles(env.Source).Where(path => Path.GetFileName(path) is "bootstrap.json" or "bootstrap.json.previous" or "bootstrap.lock" or "storage-session.lock").Sum(path => new FileInfo(path).Length);
        var all = Directory.EnumerateFiles(env.Source, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
        Assert.Equal(all - control, usage.TotalBytes);
        Assert.Equal(StorageMigrationStatus.Completed, (await runtime.GetRequiredService<IDataRootMigrationService>().MigrateAsync(env.Target)).Status);
        Assert.True(File.Exists(env.WorkingPath));
    }

    [Fact]
    public async Task CustomRootStartupReopensCommittedRootAndDoesNotCreateDefaultDatabase()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        Assert.Equal(StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        await using var runtime = await RuntimeAsync(env.Fixture);
        await DesktopStorageServices.InitializeAsync(runtime);
        await using var scope = runtime.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FinalCheckDbContext>();
        Assert.Equal(env.Target, context.ManagedPaths.CurrentDataRoot);
        Assert.Equal(2, await context.FormatRestoreOperations.CountAsync());
    }

    [Fact]
    public async Task StartupRecoversCommittedReceiptBeforeOpeningApplicationData()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        Assert.Equal(StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        var receipt = env.Journal.Load().Single();
        env.Journal.Save(receipt with { Status = StorageMigrationStatus.InProgress, Stage = StorageMigrationStage.Committing });
        await using var runtime = await RuntimeAsync(env.Fixture);
        var diagnostics = await DesktopStorageServices.InitializeAsync(runtime);
        Assert.Contains(diagnostics, item => item.Code == "CommittedMigrationReceiptRecovered");
        Assert.Equal(env.Target, runtime.GetRequiredService<IDataRootProvider>().CurrentDataRoot);
        Assert.Equal(StorageMigrationStatus.Completed, env.Journal.Load().Single().Status);
    }

    [Fact]
    public async Task StartupWithMissingExistingDatabaseFailsInsteadOfReinitializing()
    {
        using var fixture = new StorageFixture();
        var resolved = await fixture.Resolver.ResolveAsync();
        await fixture.CreateDatabaseAsync(resolved.Provider.DatabasePath);
        await fixture.Resolver.MarkDatabaseInitializedAsync();
        File.Move(resolved.Provider.DatabasePath, resolved.Provider.DatabasePath + ".preserved");
        await Assert.ThrowsAsync<InvalidDataException>(() => RuntimeAsync(fixture));
        Assert.False(File.Exists(resolved.Provider.DatabasePath));
    }

    [Fact]
    public async Task MigrationRebindsScopesAndRefreshesUsageWithoutRestart()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        await using var runtime = await RuntimeAsync(env.Fixture);
        await DesktopStorageServices.InitializeAsync(runtime);
        var usageService = runtime.GetRequiredService<IStorageUsageService>();
        var oldUsage = await usageService.CalculateAsync();
        var notifier = runtime.GetRequiredService<IStorageRootChangeNotifier>();
        DataRootDescriptor? notification = null;
        notifier.DataRootChanged += _ => throw new InvalidOperationException("UI refresh failure does not roll back committed data.");
        notifier.DataRootChanged += root =>
        {
            notification = root;
            using var newScope = runtime.CreateScope();
            Assert.Equal(root, newScope.ServiceProvider.GetRequiredService<FinalCheckDbContext>().ManagedPaths.Descriptor);
        };
        Assert.Equal(StorageMigrationStatus.Completed, (await runtime.GetRequiredService<IDataRootMigrationService>().MigrateAsync(env.Target)).Status);
        var newUsage = await usageService.CalculateAsync();
        Assert.Equal(env.Target, newUsage.Root.Path);
        Assert.Equal(oldUsage.Root.Generation + 1, newUsage.Root.Generation);
        Assert.Equal(newUsage.Root, notification);
        Assert.Equal(oldUsage.SnapshotBytes, newUsage.SnapshotBytes);
        Assert.Equal(oldUsage.ComparisonBytes, newUsage.ComparisonBytes);
        Assert.True(newUsage.BackupBytes > oldUsage.BackupBytes);
        Assert.True(newUsage.IsComplete);
    }

    [Fact]
    public async Task UsageCountsEachPhysicalFileOnceAndKeepsPayloadsInsideDatabase()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var provider = env.Coordinator;
        Directory.CreateDirectory(provider.LogPath);
        Directory.CreateDirectory(provider.TempPath);
        Directory.CreateDirectory(provider.BackupPath);
        File.WriteAllText(Path.Combine(provider.LogPath, "sample.log"), "log");
        File.WriteAllText(Path.Combine(provider.TempPath, "temp.bin"), "temp");
        File.WriteAllText(Path.Combine(provider.BackupPath, "sample.bin"), "backup");
        var candidate = Path.Combine(Path.GetDirectoryName(env.WorkingPath)!, "candidate-unused.docx");
        File.WriteAllText(candidate, "candidate");
        await using var runtime = await RuntimeAsync(env.Fixture);
        var usage = await runtime.GetRequiredService<IStorageUsageService>().CalculateAsync();
        Assert.True(usage.IsComplete);
        Assert.True(usage.PayloadBytesIncludedInDatabase);
        Assert.True(usage.SnapshotBytes > 0 && usage.ComparisonBytes > 0 && usage.RestoreBytes > 0);
        Assert.Equal(3, usage.LogBytes);
        Assert.Equal(4 + 9, usage.TempBytes);
        Assert.Equal(new FileInfo(env.WorkingPath).Length, usage.WorkingCopyBytes);
        Assert.Equal(Directory.EnumerateFiles(env.Source, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length), usage.TotalBytes);
        Assert.Equal(usage.DatabaseBytes + usage.WorkingCopyBytes + usage.BackupBytes + usage.CacheBytes + usage.LogBytes + usage.TempBytes + usage.OtherBytes, usage.TotalBytes);
    }

    [Fact]
    public async Task UsageIncludesLiveWalAndSharedMemoryFiles()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = env.Coordinator.DatabasePath, Pooling = false }.ToString());
        await writer.OpenAsync();
        using var command = writer.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; UPDATE DocumentSnapshots SET CreatedAtUtc='2026-09-13 12:00:00'";
        await command.ExecuteNonQueryAsync();
        await using var runtime = await RuntimeAsync(env.Fixture);
        var usage = await runtime.GetRequiredService<IStorageUsageService>().CalculateAsync();
        Assert.Equal(DatabaseFiles.Sum(name => new FileInfo(Path.Combine(env.Source, name)).Length), usage.DatabaseBytes);
    }

    [Fact]
    public async Task UsageMarksDatabaseReadFailureAsPartialNotFakeZeroSuccess()
    {
        using var fixture = new StorageFixture();
        await using var runtime = await RuntimeAsync(fixture, new FailedUsageReader());
        await DesktopStorageServices.InitializeAsync(runtime);
        var usage = await runtime.GetRequiredService<IStorageUsageService>().CalculateAsync();
        Assert.False(usage.IsComplete);
        Assert.Contains("DatabaseUsage:IOException", usage.Diagnostics);
        Assert.True(usage.DatabaseBytes > 0);
    }

    [Fact]
    public async Task CanceledUsageReleasesGenerationSession()
    {
        using var fixture = new StorageFixture();
        await using var runtime = await RuntimeAsync(fixture);
        await DesktopStorageServices.InitializeAsync(runtime);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.GetRequiredService<IStorageUsageService>().CalculateAsync(new CancellationToken(true)));
        using var lease = await runtime.GetRequiredService<IStorageMaintenanceCoordinator>().EnterMaintenanceAsync();
        lease.Complete();
    }

    [Theory]
    [InlineData("network")]
    [InlineData("removable")]
    [InlineData("sync")]
    [InlineData("temp")]
    public async Task ConfiguredRiskyStorageIsRejectedBeforeDataServices(string risk)
    {
        using var fixture = new StorageFixture();
        var resolved = await fixture.Resolver.ResolveAsync();
        await fixture.CreateDatabaseAsync(resolved.Provider.DatabasePath);
        await fixture.Resolver.MarkDatabaseInitializedAsync();
        var bytes = File.ReadAllBytes(resolved.Provider.DatabasePath);
        var facts = new StorageVolumeInfo(risk == "network" ? StorageDriveKind.Network : risk == "removable" ? StorageDriveKind.Removable : StorageDriveKind.Fixed,
            long.MaxValue, risk == "temp" ? fixture.DirectoryPath : Path.Combine(fixture.DirectoryPath, "system-temp"),
            risk == "sync" ? [fixture.DirectoryPath] : []);
        await Assert.ThrowsAsync<InvalidDataException>(() => RuntimeAsync(fixture, volumes: new FixtureVolumeInfoProvider(facts)));
        Assert.Equal(bytes, File.ReadAllBytes(resolved.Provider.DatabasePath));
    }

    [Fact]
    public async Task UsageExcludesKnownOriginalDocxEvenInsideDataRoot()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var inside = Path.Combine(env.Source, "user-original.docx");
        File.Copy(env.Original, inside);
        await using (var context = env.Factory.CreateDbContext())
        {
            var row = await context.RestoredWorkingCopies.SingleAsync();
            var copy = System.Text.Json.JsonSerializer.Deserialize<FinalCheck.Core.Formatting.RestoredWorkingCopy>(row.Payload)!;
            row.Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(copy with { OriginalPath = inside });
            await context.SaveChangesAsync();
        }
        await using var runtime = await RuntimeAsync(env.Fixture);
        var usage = await runtime.GetRequiredService<IStorageUsageService>().CalculateAsync();
        Assert.True(usage.IsComplete);
        Assert.Equal(Directory.EnumerateFiles(env.Source, "*", SearchOption.AllDirectories).Where(path => path != inside).Sum(path => new FileInfo(path).Length), usage.TotalBytes);
        Assert.True(File.Exists(inside));
    }

    [Fact]
    public async Task InterruptedAttemptCanBeRetriedWithoutObsoleteJournalBlockingLaterStartup()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var id = Guid.NewGuid();
        var staging = env.Target + ".interrupted-" + id.ToString("N");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "retained.txt"), "retained");
        env.Journal.Save(new(1, id, env.Coordinator.Descriptor, env.Fixture.Descriptor(env.Target) with { Generation = 2 }, staging,
            DateTimeOffset.UtcNow, null, StorageMigrationStatus.InProgress, StorageMigrationStage.Copying, null, 0, [], false));
        await using (var runtime = await RuntimeAsync(env.Fixture))
        {
            var recovery = await DesktopStorageServices.InitializeAsync(runtime);
            Assert.Contains(recovery, item => item.Code == "IncompleteMigrationOldRootActive");
            Assert.Equal(StorageMigrationStatus.Completed, (await runtime.GetRequiredService<IDataRootMigrationService>().MigrateAsync(env.Target)).Status);
        }
        await using var restarted = await RuntimeAsync(env.Fixture);
        await DesktopStorageServices.InitializeAsync(restarted);
        Assert.Equal(env.Target, restarted.GetRequiredService<IDataRootProvider>().CurrentDataRoot);
        Assert.True(File.Exists(Path.Combine(staging, "retained.txt")));
        Assert.Equal(StorageMigrationStatus.Failed, env.Journal.Load().Single(record => record.MigrationId == id).Status);
    }

    private static async Task<ServiceProvider> RuntimeAsync(StorageFixture fixture, IStorageDatabaseUsageReader? usage = null, IStorageVolumeInfoProvider? volumes = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentParser, OpenXmlDocumentParser>();
        services.AddSingleton<IDocumentSnapshotSerializer, JsonDocumentSnapshotSerializer>();
        services.AddSingleton<IComparisonResultSerializer, JsonComparisonResultSerializer>();
        await DesktopStorageServices.ConfigureAsync(services, fixture.Paths, volumes ?? new FixtureVolumeInfoProvider(new(StorageDriveKind.Fixed, long.MaxValue, Path.Combine(fixture.DirectoryPath, "system-temp"), [])));
        if (usage is not null) services.AddSingleton(usage);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
    private sealed class FailedUsageReader : IStorageDatabaseUsageReader
    {
        public Task<StorageDatabaseUsage> ReadAsync(string databasePath, CancellationToken cancellationToken = default) => throw new IOException("Injected isolated usage read failure.");
    }
}
