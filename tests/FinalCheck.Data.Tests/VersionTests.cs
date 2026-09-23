using System.Diagnostics;
using System.Text.Json;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Management;
using FinalCheck.Desktop;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FinalCheck.Data.Tests;
public sealed class VersionTests(ITestOutputHelper output)
{
    [Fact] public async Task ReorderedBatchCanImportSequentialRoundsInUserChosenQueueOrder()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(database); Guid id;
        await using (var db = fixture.OpenDatabase(database)) id = await ProjectAsync(db); using var provider = Services(fixture, database); var service = Service(provider); var file = Path.Combine(fixture.DirectoryPath, "round.docx"); ComparisonWorkflowTests.WriteDocument(file, "30");
        var imported = await service.ImportAsync(id, [new(file, ContractVersionRole.Counterparty, 2, "先显示第二轮", true), new(file, ContractVersionRole.Own, 1, "后显示第一轮", true)]);
        Assert.Equal(2, imported[0].RoundNumber); Assert.Equal(1, imported[1].RoundNumber); var rounds = await service.RoundsAsync(id); Assert.Equal(2, rounds.Count); Assert.Equal(1, rounds[0].Number); Assert.Equal(2, rounds[1].Number);
    }
    internal static ServiceProvider Services(StorageFixture fixture, string database)
    {
        var services = new ServiceCollection(); services.AddScoped(_ => fixture.OpenDatabase(database)); services.AddSingleton<IDocumentSnapshotSerializer, JsonDocumentSnapshotSerializer>();
        services.AddScoped<DocumentSnapshotStore>(); services.AddScoped<IContractVersionStore, ContractVersionStore>(); return services.BuildServiceProvider();
    }
    internal static ContractVersionService Service(ServiceProvider provider) => new(new ComparisonFileInspector(), new OpenXmlDocumentParser(), provider.GetRequiredService<IServiceScopeFactory>());
    internal static async Task<Guid> ProjectAsync(FinalCheckDbContext db) => (await new ProjectStore(db).SaveAsync(null, new("合同项目", "用户对方", "代理", null, null, "", [], ""))).ProjectId;
    [Fact] public async Task BatchExplicitRolesRoundsDuplicatesAndReloadKeepOriginalsUnchanged()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(database);
        Guid id; await using (var db = fixture.OpenDatabase(database)) id = await ProjectAsync(db);
        using var provider = Services(fixture, database); var service = Service(provider); var own = Path.Combine(fixture.DirectoryPath, "opponent-name.docx"); var other = Path.Combine(fixture.DirectoryPath, "own-name.docx");
        ComparisonWorkflowTests.WriteDocument(own, "30"); ComparisonWorkflowTests.WriteDocument(other, "60"); var before = await service.InspectAsync(own);
        var batch = await service.ImportAsync(id, [new(own, ContractVersionRole.Own, 1, "首份"), new(other, ContractVersionRole.Counterparty, 1, "对方")]);
        Assert.Equal(ContractVersionRole.Own, batch[0].Role); Assert.Equal(ContractVersionRole.Counterparty, batch[1].Role); Assert.All(batch, x => Assert.False(x.IsCurrentBaseline));
        Assert.Equal([1, 2], batch.Select(x => x.VersionNumber));
        var next = (await service.ImportAsync(id, [new(own, ContractVersionRole.Own, 2, "第二轮", true)]))[0]; Assert.Equal(batch[0].ContractVersionId, next.DuplicateReference);
        Assert.Equal(3, next.VersionNumber); Assert.Equal("V3", next.VersionLabel);
        Assert.Equal(2, (await service.RoundsAsync(id)).Count); Assert.Equal(3, (await service.ListAsync(id)).Count);
        Assert.Equal(before.Sha256, (await service.InspectAsync(own)).Sha256); Assert.Equal(next.Source.Sha256, (await service.LoadSnapshotAsync(next.ContractVersionId)).Metadata.Sha256);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(database)!, "*.docx", SearchOption.AllDirectories));
        await using var reopened = fixture.OpenDatabase(database); Assert.Empty(await reopened.ComparisonRecords.ToArrayAsync()); Assert.Null((await new ProjectStore(reopened).GetAsync(id)).CurrentBaselineVersionId);
    }
    [Fact] public async Task DuplicateInvalidRoleSkippedRoundChangedQueueAndPartialBatchFailureAreNonDestructive()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(database);
        Guid id; await using (var db = fixture.OpenDatabase(database)) id = await ProjectAsync(db); using var provider = Services(fixture, database); var service = Service(provider);
        var file = Path.Combine(fixture.DirectoryPath, "one.docx"); ComparisonWorkflowTests.WriteDocument(file, "30"); await service.ImportAsync(id, [new(file, ContractVersionRole.Own, 1, "")]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(id, [new(file, ContractVersionRole.Own, 1, "")]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(id, [new(file, (ContractVersionRole)99, 1, "", true)]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(id, [new(file, ContractVersionRole.Own, 2, "", true), new(file, ContractVersionRole.Counterparty, 4, "", true)]));
        var original = await service.InspectAsync(file); ComparisonWorkflowTests.WriteDocument(file, "60");
        await Assert.ThrowsAsync<IOException>(() => service.ImportAsync(id, [new(file, ContractVersionRole.Own, 2, "", true, original.Sha256)]));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportAsync(id, [new(file, ContractVersionRole.Own, 2, "")], cancellation.Token));
        await using var dbAfter = fixture.OpenDatabase(database); Assert.Single(await dbAfter.ContractVersions.ToArrayAsync()); Assert.Single(await dbAfter.NegotiationRounds.ToArrayAsync()); Assert.Equal(2, await dbAfter.DocumentSnapshots.CountAsync());
        Assert.Equal(2, (await dbAfter.Projects.SingleAsync(x => x.Id == id)).NextVersionNumber);
    }
    [Fact] public async Task MissingMovedSourcePreviewAndRelinkPreserveOriginalIdentity()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(database);
        Guid id; await using (var db = fixture.OpenDatabase(database)) id = await ProjectAsync(db); using var provider = Services(fixture, database); var service = Service(provider);
        var original = Path.Combine(fixture.DirectoryPath, "source.docx"); ComparisonWorkflowTests.WriteDocument(original, "30"); var version = (await service.ImportAsync(id, [new(original, ContractVersionRole.Counterparty, 1, "")]))[0];
        var renamed = Path.Combine(fixture.DirectoryPath, "renamed.docx"); File.Move(original, renamed); Assert.Single(await service.FindRelinkCandidatesAsync(version)); await service.RelinkAsync(version.ContractVersionId, renamed);
        var loaded = await service.GetAsync(version.ContractVersionId); Assert.Equal(renamed, loaded.Source.Path); Assert.Equal(original, loaded.OriginalSourceMetadata.Path);
        Assert.NotNull(await service.LoadSnapshotAsync(version.ContractVersionId)); ComparisonWorkflowTests.WriteDocument(renamed, "90"); await Assert.ThrowsAsync<IOException>(() => service.RelinkAsync(version.ContractVersionId, renamed));
    }
    [Fact] public async Task Schema6UpgradeAndStorageMigrationPreserveVersionsAndExcludeOriginalInsideRoot()
    {
        using (var fixture = new StorageFixture())
        {
            var database = Path.Combine(fixture.DirectoryPath, "legacy", "finalcheck.db"); await fixture.CreateDatabaseAsync(database, "20260913055622_AddContractProjects"); await using var db = fixture.OpenDatabase(database);
            var old = await db.DocumentSnapshots.AsNoTracking().SingleAsync(); await db.Database.MigrateAsync(); Assert.Equal(old.Payload, (await db.DocumentSnapshots.AsNoTracking().SingleAsync()).Payload); await new SqliteDataRootDatabaseInspector().ValidateAsync(database);
        }
        await using var env = await MigrationEnvironment.CreateAsync(); var source = Path.Combine(env.Source, "original-contract.docx"); ComparisonWorkflowTests.WriteDocument(source, "30"); Guid id; Guid versionId;
        await using (var db = env.Factory.CreateDbContext())
        {
            id = await ProjectAsync(db); var file = await new ComparisonFileInspector().InspectAsync(source); var snapshot = await new OpenXmlDocumentParser().ParseFileAsync(source);
            versionId = (await new ContractVersionStore(db, new DocumentSnapshotStore(db, new JsonDocumentSnapshotSerializer())).ImportAsync(id, [new(new(source, ContractVersionRole.Own, 1, ""), file, snapshot)]))[0].ContractVersionId;
        }
        Assert.Equal(Core.Storage.StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status); Assert.True(File.Exists(source)); Assert.False(File.Exists(Path.Combine(env.Target, "original-contract.docx")));
        await using var target = env.Factory.CreateDbContext(); var version = await new ContractVersionStore(target, new DocumentSnapshotStore(target, new JsonDocumentSnapshotSerializer())).GetAsync(versionId); Assert.Equal(source, version.Source.Path);
    }
    [Fact] public async Task Schema9BackfillAssignsStableChronologicalNumbersWithoutChangingLegacyIdentity()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "legacy", "finalcheck.db");
        await fixture.CreateDatabaseAsync(database, "20260913062104_AddProjectDeletionJournal");
        await using var db = fixture.OpenDatabase(database);
        var snapshot = await db.DocumentSnapshots.AsNoTracking().SingleAsync(); var payload = snapshot.Payload.ToArray();
        var projectId = Guid.NewGuid(); var roundId = Guid.NewGuid(); var earlier = Guid.NewGuid(); var later = Guid.NewGuid();
        var firstPath = Path.Combine(fixture.DirectoryPath, "legacy-first.docx");
        var secondPath = Path.Combine(fixture.DirectoryPath, "legacy-second.docx");
        var firstFile = new Core.Comparisons.ComparisonFile(firstPath, "legacy-first.docx", 1, DateTimeOffset.UtcNow, new('A', 64));
        var secondFile = new Core.Comparisons.ComparisonFile(secondPath, "legacy-second.docx", 1, DateTimeOffset.UtcNow, new('B', 64));
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Projects (Id, ProjectName, Counterparty, ContractType, TagsJson, Status, CreatedAtUtc, UpdatedAtUtc, Notes) VALUES ({projectId}, {"历史项目"}, {""}, {""}, {"[]"}, {0}, {now}, {now}, {""})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO NegotiationRounds (Id, ProjectId, Number) VALUES ({roundId}, {projectId}, {1})");
        async Task InsertAsync(Guid id, Core.Comparisons.ComparisonFile file, DateTime imported) => await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ContractVersions (Id, ProjectId, FilePath, FileName, FileSize, ModifiedAtUtc, Sha256, SnapshotId, Role, RoundNumber, ImportedAtUtc, Notes, OriginalSourceJson, ParseStatus) VALUES ({id}, {projectId}, {file.Path}, {file.Name}, {file.Size}, {file.ModifiedAt.UtcDateTime}, {file.Sha256}, {snapshot.Id}, {0}, {1}, {imported}, {""}, {JsonSerializer.Serialize(file)}, {0})");
        // Insert in the opposite order to verify the migration uses time rather than insertion order.
        await InsertAsync(later, secondFile, now); await InsertAsync(earlier, firstFile, now.AddMinutes(-1));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Projects SET CurrentBaselineVersionId = {earlier}, LastBaselineType = {(int)ProjectBaselineType.Own} WHERE Id = {projectId}");
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        Assert.Equal(payload, (await db.DocumentSnapshots.AsNoTracking().SingleAsync()).Payload);
        var rows = await db.ContractVersions.AsNoTracking().OrderBy(x => x.VersionNumber).ToArrayAsync();
        Assert.Equal([earlier, later], rows.Select(x => x.Id)); Assert.Equal([1, 2], rows.Select(x => x.VersionNumber));
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == projectId);
        Assert.Equal(3, project.NextVersionNumber); Assert.Equal(earlier, project.CurrentBaselineVersionId);
        Assert.All(rows, row => { Assert.Equal(1, row.RoundNumber); Assert.Equal(0, row.Role); });
        await new SqliteDataRootDatabaseInspector().ValidateAsync(database);
    }
    [Fact] public async Task FiftyVersionsAndTwentyTemplatesUseMetadataPagesWithoutSnapshotDecode()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(database); await using var db = fixture.OpenDatabase(database);
        var id = await ProjectAsync(db); var store = new ContractVersionStore(db, new DocumentSnapshotStore(db, new JsonDocumentSnapshotSerializer()));
        var file = new Core.Comparisons.ComparisonFile(Path.Combine(fixture.DirectoryPath, "not-read.docx"), "not-read.docx", 1, DateTimeOffset.UtcNow, new('A', 64));
        var snapshot = Core.Documents.DocumentSnapshot.Empty with { Metadata = Core.Documents.DocumentMetadataSnapshot.Empty with { Sha256 = file.Sha256 } };
        await store.ImportAsync(id, Enumerable.Range(0, 50).Select(i => new PreparedVersion(new(file.Path, i % 2 == 0 ? ContractVersionRole.Own : ContractVersionRole.Counterparty, 1, "", true), file, snapshot)).ToArray());
        for (var i = 0; i < 20; i++) await ProjectTests.TemplateAsync(db, $"模板{i}");
        await db.DocumentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(x => x.Payload, new byte[] { 0 })); db.ChangeTracker.Clear();
        var watch = Stopwatch.StartNew(); var page = await store.ListAsync(id); watch.Stop(); Assert.Equal(20, page.Count); Assert.Equal(50, (await store.ListAsync(id, limit: 100)).Count); Assert.Equal(20, (await new TemplateStore(db, new DocumentSnapshotStore(db, new JsonDocumentSnapshotSerializer())).ListAsync()).Count);
        output.WriteLine($"50 versions / 20 metadata rows: {watch.Elapsed.TotalMilliseconds:F1} ms.");
    }
}
