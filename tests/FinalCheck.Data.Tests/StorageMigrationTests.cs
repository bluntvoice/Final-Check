using FinalCheck.Core.Storage;
using FinalCheck.Core.Formatting;
using FinalCheck.Core.Documents;
using FinalCheck.Documents;
using FinalCheck.Comparison;
using FinalCheck.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data.Tests;

public sealed class StorageMigrationTests
{
    [Fact]
    public async Task RealHistoryFilesAndUndoSurviveMigrationAndOldDataRemains()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var original = File.ReadAllBytes(env.Original);
        var working = File.ReadAllBytes(env.WorkingPath);
        var result = await env.Migration().MigrateAsync(env.Target);
        Assert.Equal(StorageMigrationStatus.Completed, result.Status);
        Assert.Equal(env.Target, env.Coordinator.CurrentDataRoot);
        Assert.Equal(env.Target, env.Fixture.Bootstrap.Load()!.Current.Path);
        Assert.Equal(env.Target, env.Fixture.Bootstrap.Load()!.LastKnownGood.Path);
        Assert.Equal(original, File.ReadAllBytes(env.Original));
        Assert.Equal(working, File.ReadAllBytes(env.WorkingPath));
        Assert.Equal("keep-cache", File.ReadAllText(Path.Combine(env.Target, "Cache", "cache.bin")));
        await using var source = env.Fixture.OpenDatabase(Path.Combine(env.Source, "finalcheck.db"));
        await using var target = env.Factory.CreateDbContext();
        Assert.Equal(await source.DocumentSnapshots.CountAsync(), await target.DocumentSnapshots.CountAsync());
        Assert.Equal(await source.ComparisonResults.CountAsync(), await target.ComparisonResults.CountAsync());
        Assert.Equal(await source.FormatRestoreOperations.CountAsync(), await target.FormatRestoreOperations.CountAsync());
        var store = new FormatRestoreStore(target, new JsonDocumentSnapshotSerializer(), new JsonComparisonResultSerializer());
        var copy = await store.LoadWorkingCopyAsync(env.Version);
        Assert.StartsWith(Path.Combine(env.Target, "WorkingCopies"), copy!.WorkingPath);
        Assert.Equal(env.Original, copy.OriginalPath);
        Assert.Equal(working, File.ReadAllBytes(copy.WorkingPath));
        var undo = await env.NewWorkingService(target).UndoLastAsync(env.Version);
        Assert.Equal(FormatRestoreResultStatus.Completed, undo.Status);
        Assert.True(undo.WorkingCopy!.Snapshot.Paragraphs[0].Runs[0].EffectiveFormatting.Bold);
        Assert.Equal(original, File.ReadAllBytes(env.Original));
        Assert.Equal(working, File.ReadAllBytes(env.WorkingPath));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(env.Target, "Backups"), "source-finalcheck.db", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task UncheckpointedWalAndActiveReadonlyConnectionAreIncludedByBackupApi()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(env.Source, "finalcheck.db"), Pooling = false }.ToString());
        await writer.OpenAsync();
        using var command = writer.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; UPDATE DocumentSnapshots SET CreatedAtUtc='2026-09-12 12:00:00'";
        await command.ExecuteNonQueryAsync();
        Assert.True(new FileInfo(Path.Combine(env.Source, "finalcheck.db-wal")).Length > 0);
        var result = await env.Migration().MigrateAsync(env.Target);
        Assert.Equal(StorageMigrationStatus.Completed, result.Status);
        await using var target = env.Factory.CreateDbContext();
        Assert.All(await target.DocumentSnapshots.ToArrayAsync(), row => Assert.Equal(12, row.CreatedAtUtc.Hour));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCopyOrCancellationKeepsOldRootAndStaging(bool cancel)
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var result = await env.Migration(copier: new FaultingCopier(cancellation, cancel)).MigrateAsync(env.Target, cancellation.Token);
        Assert.Equal(cancel ? StorageMigrationStatus.Cancelled : StorageMigrationStatus.Failed, result.Status);
        env.AssertOldActive();
        Assert.True(Directory.Exists(env.Journal.Load().Single().StagingPath));
        Assert.Equal(2, await env.CountSourceRestoresAsync());
    }

    [Fact]
    public async Task IntegrityFailureNeverPublishesNewRoot()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var result = await env.Migration(database: new CorruptingDatabaseService(env.Database)).MigrateAsync(env.Target);
        Assert.Equal(StorageMigrationStatus.Failed, result.Status);
        env.AssertOldActive();
        Assert.Equal(2, await env.CountSourceRestoresAsync());
    }

    [Fact]
    public async Task SpaceFailureAndEarlyCancellationNeverCreateTargetData()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var result = await env.Migration(available: 1).MigrateAsync(env.Target);
        Assert.Equal("InsufficientSpace", result.Code);
        Assert.False(Directory.Exists(env.Target));
        var canceled = await env.Migration().MigrateAsync(env.Target, new CancellationToken(true));
        Assert.Equal(StorageMigrationStatus.Cancelled, canceled.Status);
        env.AssertOldActive();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapFailureUsesDurableCommitEvidenceWithoutGuessing(bool afterCommit)
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var fault = new FaultingBootstrap(env.Fixture.Bootstrap, afterCommit);
        var result = await env.Migration(bootstrap: fault).MigrateAsync(env.Target);
        Assert.Equal(afterCommit ? StorageMigrationStatus.Completed : StorageMigrationStatus.Failed, result.Status);
        if (afterCommit) Assert.Equal(env.Target, env.Coordinator.CurrentDataRoot); else env.AssertOldActive();
        Assert.True(File.Exists(env.WorkingPath));
    }

    [Fact]
    public async Task InterruptedMigrationRecoveryKeepsOldRootAndUnfinishedTarget()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var id = Guid.NewGuid();
        var staging = env.Target + ".finalcheck-migration-" + id.ToString("N");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "retained.txt"), "retained");
        env.Journal.Save(new(1, id, env.Coordinator.Descriptor, env.Fixture.Descriptor(env.Target) with { Generation = 2 }, staging,
            DateTimeOffset.UtcNow, null, StorageMigrationStatus.InProgress, StorageMigrationStage.Copying, null, 0, [], false));
        var diagnostics = await env.Recovery().RecoverAsync();
        Assert.Contains(diagnostics, item => item.Code == "IncompleteMigrationOldRootActive");
        Assert.True(File.Exists(Path.Combine(staging, "retained.txt")));
        env.AssertOldActive();
    }

    [Fact]
    public async Task CommittedCrashReceiptRecoveryPreservesNewerWritesInsteadOfFallingBack()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        Assert.Equal(StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        var receipt = env.Journal.Load().Single();
        env.Journal.Save(receipt with { Status = StorageMigrationStatus.InProgress, Stage = StorageMigrationStage.Committing });
        await using (var context = env.Factory.CreateDbContext())
            await new DocumentSnapshotStore(context, new JsonDocumentSnapshotSerializer()).SaveAsync(DocumentSnapshot.Empty);
        var diagnostics = await env.Recovery().RecoverAsync();
        Assert.Contains(diagnostics, item => item.Code == "CommittedMigrationReceiptRecovered");
        await using var current = env.Factory.CreateDbContext();
        Assert.Equal(3, await current.DocumentSnapshots.CountAsync());
        Assert.Equal(env.Target, env.Fixture.Bootstrap.Load()!.Current.Path);
    }

    [Fact]
    public async Task MaintenanceDrainsScopesBlocksNewOnesAndRejectsDisposedPaths()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var context = env.Factory.CreateDbContext();
        var paths = context.ManagedPaths;
        var entering = env.Coordinator.EnterMaintenanceAsync();
        Assert.False(entering.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => env.Factory.CreateDbContext());
        await context.DisposeAsync();
        using (await entering) { Assert.Throws<InvalidOperationException>(() => env.Factory.CreateDbContext()); }
        Assert.Throws<ObjectDisposedException>(() => _ = paths.DatabasePath);
        await using var reopened = env.Factory.CreateDbContext();
        Assert.Equal(2, await reopened.FormatRestoreOperations.CountAsync());
    }

    [Fact]
    public async Task CooperativeProcessCannotWriteDuringAnotherProcessSessionAndReloadsCommittedRoot()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        using var other = new StorageMaintenanceCoordinator(new DataRootPaths(env.Coordinator.Descriptor), env.Fixture.Paths, env.Fixture.Bootstrap, env.Fixture.Resolver);
        using (env.Coordinator.OpenSession()) Assert.Throws<IOException>(() => other.OpenSession());
        Assert.Equal(StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        using var reloaded = other.OpenSession();
        Assert.Equal(env.Target, reloaded.CurrentDataRoot);
        Assert.Equal(env.Coordinator.Descriptor, reloaded.Descriptor);
    }

    [Fact]
    public async Task CancellationWhileDrainingLeavesExistingScopeUsable()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        await using var context = env.Factory.CreateDbContext();
        using var cancellation = new CancellationTokenSource();
        var entering = env.Coordinator.EnterMaintenanceAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => entering);
        await new DocumentSnapshotStore(context, new JsonDocumentSnapshotSerializer()).SaveAsync(DocumentSnapshot.Empty);
        env.AssertOldActive();
    }

    [Fact]
    public async Task UnknownRootFilesArePreservedAndBlockImplicitMigration()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var userFile = Path.Combine(env.Source, "user.keep");
        File.WriteAllText(userFile, "never drop");
        Assert.Equal(StorageMigrationStatus.Failed, (await env.Migration().MigrateAsync(env.Target)).Status);
        Assert.Equal("never drop", File.ReadAllText(userFile));
        env.AssertOldActive();
    }

    [Fact]
    public async Task OriginalDocxInsideLegacyRootIsNotCopiedOrRepointed()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var inside = Path.Combine(env.Source, "user-original.docx");
        File.Copy(env.Original, inside);
        await using (var context = env.Factory.CreateDbContext())
        {
            var row = await context.RestoredWorkingCopies.SingleAsync();
            var copy = System.Text.Json.JsonSerializer.Deserialize<RestoredWorkingCopy>(row.Payload)!;
            row.Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(copy with { OriginalPath = inside });
            // Historical operations must retain exactly the same original identity as current metadata.
            foreach (var operationRow in await context.FormatRestoreOperations.ToArrayAsync())
            {
                var operation = System.Text.Json.JsonSerializer.Deserialize<FormatRestoreOperation>(operationRow.Payload)!;
                operationRow.Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(operation with { WorkingCopy = operation.WorkingCopy with { OriginalPath = inside } });
            }
            await context.SaveChangesAsync();
        }
        var before = File.ReadAllBytes(inside);
        Assert.Equal(StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        Assert.Equal(before, File.ReadAllBytes(inside));
        Assert.False(File.Exists(Path.Combine(env.Target, "user-original.docx")));
        await using var target = env.Factory.CreateDbContext();
        var payload = (await target.RestoredWorkingCopies.SingleAsync()).Payload;
        Assert.Equal(inside, System.Text.Json.JsonSerializer.Deserialize<RestoredWorkingCopy>(payload)!.OriginalPath);
    }

    [Fact]
    public async Task LegacySnapshotPayloadRemainsByteIdenticalWithoutProductSchemaUpgrade()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        var legacy = System.Text.Encoding.UTF8.GetBytes("{\"snapshotSchemaVersion\":1,\"paragraphs\":[],\"tables\":[],\"revisions\":[],\"comments\":[]}");
        Guid id;
        await using (var context = env.Factory.CreateDbContext())
        {
            var row = await context.DocumentSnapshots.FirstAsync();
            id = row.Id; row.SnapshotSchemaVersion = 1; row.Payload = legacy;
            await context.SaveChangesAsync();
        }
        Assert.Equal(StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        await using var target = env.Factory.CreateDbContext();
        var loaded = await target.DocumentSnapshots.SingleAsync(row => row.Id == id);
        Assert.Equal(1, loaded.SnapshotSchemaVersion);
        Assert.Equal(legacy, loaded.Payload);
    }

    [Fact]
    public async Task FailedCommittedRecoveryBlocksFurtherWritesInsteadOfSwitchingToOldData()
    {
        await using var env = await MigrationEnvironment.CreateAsync();
        Assert.Equal(StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        var receipt = env.Journal.Load().Single();
        env.Journal.Save(receipt with { Status = StorageMigrationStatus.InProgress, Stage = StorageMigrationStage.Committing });
        await using (var context = env.Factory.CreateDbContext())
        {
            var row = await context.DocumentSnapshots.FirstAsync(); row.SnapshotSchemaVersion = 99;
            await context.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => env.Recovery().RecoverAsync());
        Assert.Throws<InvalidOperationException>(() => env.Factory.CreateDbContext());
        Assert.Equal(env.Target, env.Fixture.Bootstrap.Load()!.Current.Path);
        Assert.True(File.Exists(env.WorkingPath));
    }

    private sealed class FaultingCopier(CancellationTokenSource cancellation, bool cancel) : IStorageMigrationFileCopier
    {
        public async Task CopyAsync(string source, string target, CancellationToken token)
        {
            if (!cancel) throw new IOException("Injected copy failure.");
            await new StorageMigrationFileCopier().CopyAsync(source, target, token);
            cancellation.Cancel();
        }
    }
    private sealed class FaultingBootstrap(IStorageBootstrapStore inner, bool after) : IStorageBootstrapStore
    {
        public StorageBootstrap? Load() => inner.Load();
        public void Save(StorageBootstrap value, long? expected)
        {
            if (after) inner.Save(value, expected);
            throw new IOException("Injected locator publication failure.");
        }
    }
    private sealed class CorruptingDatabaseService(IStorageDatabaseMigrationService inner) : IStorageDatabaseMigrationService
    {
        public async Task<IStorageDatabaseMigrationSession> OpenSourceAsync(string root, CancellationToken cancellationToken = default)
            => new CorruptingSession(await inner.OpenSourceAsync(root, cancellationToken));
        private sealed class CorruptingSession(IStorageDatabaseMigrationSession inner) : IStorageDatabaseMigrationSession
        {
            public IReadOnlySet<string> OriginalPaths => inner.OriginalPaths;
            public async Task BackupAsync(string staging, Guid id, CancellationToken cancellationToken = default)
            {
                await inner.BackupAsync(staging, id, cancellationToken);
                File.WriteAllBytes(Path.Combine(staging, "finalcheck.db"), [0, 1, 2]);
            }
            public Task RelocateAndValidateAsync(string root, string logical, CancellationToken cancellationToken = default) => inner.RelocateAndValidateAsync(root, logical, cancellationToken);
            public Task ValidateReopenedAsync(string root, CancellationToken cancellationToken = default) => inner.ValidateReopenedAsync(root, cancellationToken);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}

internal sealed class MigrationEnvironment : IAsyncDisposable
{
    public StorageFixture Fixture { get; } = new();
    public StorageMaintenanceCoordinator Coordinator { get; private set; } = null!;
    public DataRootDbContextFactory Factory => new(Coordinator, Coordinator);
    public string Source { get; private set; } = null!;
    public string Target => Path.Combine(Fixture.DirectoryPath, "target-data");
    public string Original => Path.Combine(Fixture.DirectoryPath, "original.docx");
    public string WorkingPath => Path.Combine(Source, "WorkingCopies", Version.ToString("N"), "restored.docx");
    public Guid Version { get; } = Guid.NewGuid();
    public StorageMigrationJournal Journal => new(Fixture.Paths);
    public SqliteStorageDatabaseMigrationService Database { get; } = new(new JsonDocumentSnapshotSerializer(), new JsonComparisonResultSerializer(), new OpenXmlDocumentParser());
    public static async Task<MigrationEnvironment> CreateAsync(bool legacy = false)
    {
        var env = new MigrationEnvironment();
        if (legacy) await env.Fixture.CreateDatabaseAsync(Path.Combine(env.Fixture.Paths.LegacyDataRoot, "finalcheck.db"));
        var resolution = await env.Fixture.Resolver.ResolveAsync();
        env.Source = resolution.Provider.CurrentDataRoot;
        if (!legacy) await env.Fixture.CreateDatabaseAsync(resolution.Provider.DatabasePath);
        await env.Fixture.Resolver.MarkDatabaseInitializedAsync();
        env.Coordinator = new(resolution.Provider, env.Fixture.Paths, env.Fixture.Bootstrap, env.Fixture.Resolver);
        var baselinePath = Path.Combine(env.Fixture.DirectoryPath, "baseline.docx");
        StoragePathAdoptionTests.WriteDocument(env.Original, false);
        StoragePathAdoptionTests.WriteDocument(baselinePath, true);
        var parser = new OpenXmlDocumentParser();
        var current = await parser.ParseFileAsync(env.Original);
        var baseline = await parser.ParseFileAsync(baselinePath);
        var engine = StoragePathAdoptionTests.CreateEngine();
        await using (var context = env.Factory.CreateDbContext())
        {
            await new DocumentSnapshotStore(context, new JsonDocumentSnapshotSerializer()).SaveAsync(current);
            await new ComparisonResultStore(context, new JsonComparisonResultSerializer()).SaveAsync(engine.Compare(baseline, current));
            var service = env.NewWorkingService(context);
            var first = await service.ExecuteAsync(env.Version, env.Original, new SnapshotFormatRestorePlanner().Generate(baseline, current, engine.Compare(baseline, current)));
            Assert.Equal(FormatRestoreResultStatus.Completed, first.Status);
            var restored = first.WorkingCopy!.Snapshot;
            var second = await service.ExecuteAsync(env.Version, env.Original, new SnapshotFormatRestorePlanner().Generate(current, restored, engine.Compare(current, restored)));
            Assert.Equal(FormatRestoreResultStatus.Completed, second.Status);
        }
        Directory.CreateDirectory(Path.Combine(env.Source, "Cache"));
        File.WriteAllText(Path.Combine(env.Source, "Cache", "cache.bin"), "keep-cache");
        return env;
    }
    public FormatRestoreWorkingCopyService NewWorkingService(FinalCheckDbContext context)
    {
        Assert.Equal(Coordinator.Descriptor, context.ManagedPaths.Descriptor);
        var parser = new OpenXmlDocumentParser();
        return new(new PlatformAppDataPathProvider(context.ManagedPaths), parser, new OpenXmlFormatRestoreRenderer(parser),
            new FormatRestoreStore(context, new JsonDocumentSnapshotSerializer(), new JsonComparisonResultSerializer()),
            new WorkingCopyComparisonService(StoragePathAdoptionTests.CreateEngine()));
    }
    public DataRootMigrationService Migration(IStorageMigrationFileCopier? copier = null, IStorageDatabaseMigrationService? database = null,
        IStorageBootstrapStore? bootstrap = null, long available = long.MaxValue) => new(Coordinator, Fixture.Paths,
        new DataRootValidator(Fixture.Paths, new FixtureVolumeInfoProvider(new(StorageDriveKind.Fixed, available, Path.Combine(Fixture.DirectoryPath, "system-temp"), [])), new StorageDirectoryProbe()),
        Coordinator, bootstrap ?? Fixture.Bootstrap, database ?? Database, Journal, copier);
    public StorageMigrationRecoveryService Recovery() => new(Coordinator, Fixture.Paths, Fixture.Bootstrap, Coordinator, Database, Journal);
    public void AssertOldActive()
    {
        Assert.Equal(Source, Fixture.Bootstrap.Load()!.Current.Path);
        Assert.Equal(Source, Coordinator.CurrentDataRoot);
        Assert.True(File.Exists(WorkingPath));
    }
    public async Task<int> CountSourceRestoresAsync()
    {
        await using var context = Fixture.OpenDatabase(Path.Combine(Source, "finalcheck.db"));
        return await context.FormatRestoreOperations.CountAsync();
    }
    public ValueTask DisposeAsync() { Coordinator.Dispose(); Fixture.Dispose(); return ValueTask.CompletedTask; }
}
