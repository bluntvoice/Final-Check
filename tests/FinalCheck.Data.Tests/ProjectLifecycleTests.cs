using System.Security.Cryptography;
using System.Text.Json;
using FinalCheck.Comparison;
using FinalCheck.Core.Management;
using FinalCheck.Desktop;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Data.Tests;
public sealed class ProjectLifecycleTests
{
    [Fact] public async Task ActualFormatRestoreRecordsAndWorkingCopyArePurgedWithoutTouchingOriginal()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); using var provider = ProjectComparisonTests.Services(env); var source = Path.Combine(env.Fixture.DirectoryPath, "restore-current.docx"); var baseline = Path.Combine(env.Fixture.DirectoryPath, "restore-template.docx");
        StoragePathAdoptionTests.WriteDocument(source, false); StoragePathAdoptionTests.WriteDocument(baseline, true); Guid project;
        await using (var db = env.Factory.CreateDbContext()) project = await VersionTests.ProjectAsync(db);
        var versions = new ContractVersionService(new ComparisonFileInspector(), new OpenXmlDocumentParser(), provider.GetRequiredService<IServiceScopeFactory>());
        var version = (await versions.ImportAsync(project, [new(source, ContractVersionRole.Own, 1, "")]))[0]; string working;
        await using (var db = env.Factory.CreateDbContext())
        {
            var parser = new OpenXmlDocumentParser(); var b = await parser.ParseFileAsync(baseline); var c = await parser.ParseFileAsync(source); var plan = new SnapshotFormatRestorePlanner().Generate(b, c, StoragePathAdoptionTests.CreateEngine().Compare(b, c));
            var restore = new FormatRestoreWorkingCopyService(new PlatformAppDataPathProvider(db.ManagedPaths), parser, new OpenXmlFormatRestoreRenderer(parser), new FormatRestoreStore(db, new JsonDocumentSnapshotSerializer(), new JsonComparisonResultSerializer()), new WorkingCopyComparisonService(StoragePathAdoptionTests.CreateEngine()));
            var result = await restore.ExecuteAsync(version.ContractVersionId, source, plan); Assert.Equal(Core.Formatting.FormatRestoreResultStatus.Completed, result.Status); working = result.WorkingCopy!.WorkingPath;
        }
        var lifecycle = new ProjectLifecycleService(provider.GetRequiredService<IServiceScopeFactory>()); Assert.Equal(working, (await lifecycle.RestoreStateAsync(version.ContractVersionId)).WorkingPath); await lifecycle.SetStatusAsync(project, ProjectStatus.Recycled);
        var deleted = await lifecycle.DeleteAsync(project, true); Assert.Equal("Completed", deleted.Status); Assert.False(File.Exists(working)); Assert.Equal(version.Source.Sha256, (await new ComparisonFileInspector().InspectAsync(source)).Sha256);
        await using var after = env.Factory.CreateDbContext(); Assert.False(await after.FormatRestoreOperations.AnyAsync(x => x.ContractVersionId == version.ContractVersionId)); Assert.False(await after.RestoredWorkingCopies.AnyAsync(x => x.ContractVersionId == version.ContractVersionId));
        Assert.Equal(2, await after.FormatRestoreOperations.CountAsync()); Assert.Single(await after.RestoredWorkingCopies.ToArrayAsync()); // Unrelated existing restore history must remain.
    }
    [Fact] public async Task SummaryArchiveRecycleRestoreAndRestartPreserveHistory()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); using var provider = ProjectComparisonTests.Services(env); var chain = await ProjectComparisonTests.ChainAsync(env, provider);
        var comparisons = ProjectComparisonTests.Service(provider); await comparisons.SetBaselineAsync(chain.Project, chain.Versions[2].ContractVersionId);
        var saved = await comparisons.CompareAsync(new(chain.Project, chain.Versions[3].ContractVersionId, ProjectBaselineType.Own, chain.Versions[2].ContractVersionId));
        var service = new ProjectLifecycleService(provider.GetRequiredService<IServiceScopeFactory>()); var summary = await service.SummaryAsync(chain.Project); Assert.Equal(chain.Versions[2].Source.Name, summary.CurrentBaseline); Assert.Equal(chain.Versions[3].Source.Name, summary.LatestVersion); Assert.Equal(saved.Record.RecordId, summary.LatestComparison!.RecordId); Assert.Equal(0, summary.PendingRestoreCount);
        await service.SetStatusAsync(chain.Project, ProjectStatus.Archived); await using (var db = env.Factory.CreateDbContext()) { Assert.Empty(await new ProjectStore(db).ListAsync(new())); Assert.Single(await new ProjectStore(db).ListAsync(new(Status: ProjectStatus.Archived))); }
        await service.SetStatusAsync(chain.Project, ProjectStatus.Active); await service.SetStatusAsync(chain.Project, ProjectStatus.Recycled); await service.SetStatusAsync(chain.Project, ProjectStatus.Active); Assert.Single(await comparisons.HistoryAsync(chain.Project));
        Assert.Null((await service.RestoreStateAsync(chain.Versions[3].ContractVersionId)).WorkingPath);
    }
    [Fact] public async Task PermanentDeleteRequiresRecycleAndConfirmationProtectsSharedTemplateQuickCompareAndOriginals()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); using var provider = ProjectComparisonTests.Services(env); var chain = await ProjectComparisonTests.ChainAsync(env, provider); var service = new ProjectLifecycleService(provider.GetRequiredService<IServiceScopeFactory>());
        var comparisons = ProjectComparisonTests.Service(provider); var projectRecord = await comparisons.CompareAsync(new(chain.Project, chain.Versions[1].ContractVersionId, ProjectBaselineType.Template, chain.Template.Current!.TemplateVersionId)); Guid quickId;
        await using (var db = env.Factory.CreateDbContext())
        {
            var snapshot = await new DocumentSnapshotStore(db, new JsonDocumentSnapshotSerializer()).LoadAsync(chain.Template.Current.SnapshotId);
            var quick = await new ComparisonRecordStore(db, new JsonDocumentSnapshotSerializer(), new JsonComparisonResultSerializer()).SaveAsync(chain.Template.Current.Source, chain.Template.Current.Source, snapshot!, snapshot!, StoragePathAdoptionTests.CreateEngine().Compare(snapshot!, snapshot!)); quickId = quick.Record.RecordId;
        }
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(chain.Project, false)); await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(chain.Project, true));
        await service.SetStatusAsync(chain.Project, ProjectStatus.Recycled); var deleted = await service.DeleteAsync(chain.Project, true); Assert.Equal("Completed", deleted.Status);
        await using var after = env.Factory.CreateDbContext(); Assert.Empty(await after.Projects.ToArrayAsync()); Assert.Empty(await after.ContractVersions.ToArrayAsync()); Assert.Empty(await after.NegotiationRounds.ToArrayAsync()); Assert.Empty(await after.ProjectComparisons.ToArrayAsync()); Assert.False(await after.ComparisonRecords.AnyAsync(x => x.Id == projectRecord.Record.RecordId));
        Assert.NotNull(await new ComparisonRecordStore(after, new JsonDocumentSnapshotSerializer(), new JsonComparisonResultSerializer()).LoadAsync(quickId)); Assert.True(await after.DocumentSnapshots.AnyAsync(x => x.Id == chain.Template.Current.SnapshotId)); Assert.Single(await after.ProjectDeletionOperations.ToArrayAsync());
        foreach (var version in chain.Versions) Assert.Equal(version.Source.Sha256, (await new ComparisonFileInspector().InspectAsync(version.Source.Path)).Sha256);
        Assert.True(File.Exists(chain.Template.Current.Source.Path)); await new SqliteDataRootDatabaseInspector().ValidateAsync(env.Coordinator.DatabasePath);
        await after.DisposeAsync(); // Release the managed data session before requesting Storage maintenance.
        Assert.Equal(Core.Storage.StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status); Assert.True(File.Exists(chain.Versions[0].Source.Path));
    }
    [Fact] public async Task PreparedRestoreBlocksPurgeAndCancelledDeleteDoesNotChangeProject()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); using var provider = ProjectComparisonTests.Services(env); var chain = await ProjectComparisonTests.ChainAsync(env, provider); var service = new ProjectLifecycleService(provider.GetRequiredService<IServiceScopeFactory>()); await service.SetStatusAsync(chain.Project, ProjectStatus.Recycled);
        await using (var db = env.Factory.CreateDbContext()) { db.FormatRestoreOperations.Add(new() { Id = Guid.NewGuid(), ContractVersionId = chain.Versions[0].ContractVersionId, Status = "Prepared", SchemaVersion = 1, Payload = [], CreatedAtUtc = DateTime.UtcNow }); await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(chain.Project, true)); using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteAsync(chain.Project, true, cancel.Token));
        await using var after = env.Factory.CreateDbContext(); Assert.Single(await after.Projects.ToArrayAsync()); Assert.Empty(await after.ProjectDeletionOperations.ToArrayAsync());
    }
    [Fact] public async Task VerifiedFileCleanupRetriesInterruptedStagingAndProtectsChangedUnknownOrOriginalFiles()
    {
        using var fixture = new StorageFixture(); var paths = new Core.Storage.DataRootPaths(new(fixture.DirectoryPath, Guid.NewGuid(), 1, 1)); var version = Guid.NewGuid(); var directory = Path.Combine(paths.WorkingCopyPath, version.ToString("N")); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "restored.docx"); byte[] bytes = [1, 2, 3]; await File.WriteAllBytesAsync(file, bytes); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var record = new ProjectDeletionRecord(1, Guid.NewGuid(), Guid.NewGuid(), [version], [new(Path.GetRelativePath(paths.CurrentDataRoot, file), hash)], [], DateTimeOffset.UtcNow, "PendingCleanup", "");
        using (ManagedProjectFileCleanup.LockVersions(paths, [version])) Assert.Equal("Completed", (await ManagedProjectFileCleanup.RunAsync(record, paths)).Status); Assert.False(File.Exists(file)); Assert.Equal("Completed", (await ManagedProjectFileCleanup.RunAsync(record, paths)).Status);
        var staged = Path.Combine(paths.BackupPath, "ProjectDeletion", record.OperationId.ToString("N"), version.ToString("N"), "restored.docx"); Directory.CreateDirectory(Path.GetDirectoryName(staged)!); await File.WriteAllBytesAsync(staged, bytes); Assert.Equal("Completed", (await ManagedProjectFileCleanup.RunAsync(record, paths)).Status); Assert.False(File.Exists(staged));
        await File.WriteAllBytesAsync(file, [4, 5, 6]); Assert.Equal("NeedsReview", (await ManagedProjectFileCleanup.RunAsync(record, paths)).Status); Assert.True(File.Exists(file));
        await File.WriteAllBytesAsync(file, bytes); Assert.Equal("NeedsReview", (await ManagedProjectFileCleanup.RunAsync(record with { OriginalPaths = [file] }, paths)).Status); Assert.True(File.Exists(file));
        var unsafeRecord = record with { Files = [new("../outside.docx", hash)] }; Assert.Equal("NeedsReview", (await ManagedProjectFileCleanup.RunAsync(unsafeRecord, paths)).Status); Assert.True(File.Exists(file));
    }
    [Fact] public async Task Schema8UpgradePreservesPayloadAndDeletionJournalCanRecoverAfterReopen()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "legacy", "finalcheck.db"); await fixture.CreateDatabaseAsync(database, "20260913061331_AddProjectComparisonContext");
        await using var db = fixture.OpenDatabase(database); var payload = (await db.DocumentSnapshots.AsNoTracking().SingleAsync()).Payload; await db.Database.MigrateAsync(); Assert.Equal(payload, (await db.DocumentSnapshots.AsNoTracking().SingleAsync()).Payload);
        var journal = new ProjectDeletionRecord(1, Guid.NewGuid(), Guid.NewGuid(), [], [], [Path.Combine(fixture.DirectoryPath, "original.docx")], DateTimeOffset.UtcNow, "PendingCleanup", "");
        db.ProjectDeletionOperations.Add(new() { Id = journal.OperationId, ProjectId = journal.ProjectId, Status = journal.Status, Payload = JsonSerializer.SerializeToUtf8Bytes(journal), CreatedAtUtc = journal.CreatedAt.UtcDateTime }); await db.SaveChangesAsync();
        await using var reopened = fixture.OpenDatabase(database); var store = new ProjectLifecycleStore(reopened, new JsonDocumentSnapshotSerializer(), new JsonComparisonResultSerializer()); Assert.Single(await store.PendingCleanupAsync()); await store.SaveCleanupAsync(journal with { Status = "Completed" }); Assert.Empty(await store.PendingCleanupAsync());
    }
}
