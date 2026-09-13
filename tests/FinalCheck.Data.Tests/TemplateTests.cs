using FinalCheck.Core.Management;
using FinalCheck.Data;
using FinalCheck.Documents;
using FinalCheck.Desktop;
using FinalCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Data.Tests;

public sealed class TemplateTests
{
    private static ServiceProvider Services(StorageFixture fixture, string database)
    {
        var services = new ServiceCollection(); services.AddScoped(_ => fixture.OpenDatabase(database));
        services.AddSingleton<Core.Abstractions.IDocumentSnapshotSerializer, JsonDocumentSnapshotSerializer>();
        services.AddScoped<DocumentSnapshotStore>(); services.AddScoped<ITemplateStore, TemplateStore>(); return services.BuildServiceProvider();
    }
    [Fact] public async Task ImportVersionsSwitchCurrentDisableAndReloadWithoutCopyingOriginal()
    {
        using var fixture = new StorageFixture(); var db = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(db);
        using var provider = Services(fixture, db); var service = new TemplateService(new ComparisonFileInspector(), new OpenXmlDocumentParser(), provider.GetRequiredService<IServiceScopeFactory>());
        var source = Path.Combine(fixture.DirectoryPath, "template.docx"); StoragePathAdoptionTests.WriteDocument(source, true);
        var first = await service.ImportAsync(null, "标准协议", "代理", "1.0.0", source); var second = await service.ImportAsync(first.Template.TemplateId, "标准协议", "代理", "1.0.1", source);
        Assert.Equal(2, second.Versions.Count); Assert.Equal(first.Current!.TemplateVersionId, second.Current!.TemplateVersionId);
        var next = second.Versions.Single(v => v.Version == "1.0.1"); await service.SetCurrentAsync(first.Template.TemplateId, next.TemplateVersionId);
        await service.UpdateAsync(first.Template.TemplateId, "标准协议新版", "运输", "备注", false);
        var loaded = await service.GetAsync(first.Template.TemplateId); Assert.False(loaded.Template.IsEnabled); Assert.Equal(next.TemplateVersionId, loaded.Current!.TemplateVersionId);
        Assert.Equal("标准协议新版", loaded.Template.Name); Assert.Equal("运输", loaded.Template.ContractType);
        Assert.Equal(next.Source.Sha256, (await service.LoadSnapshotAsync(next.TemplateVersionId)).Metadata.Sha256);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(db)!, "*.docx", SearchOption.AllDirectories));
    }
    [Fact] public async Task DuplicateVersionAndCancelledImportDoNotLeaveSnapshots()
    {
        using var fixture = new StorageFixture(); var db = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(db);
        using var provider = Services(fixture, db); var service = new TemplateService(new ComparisonFileInspector(), new OpenXmlDocumentParser(), provider.GetRequiredService<IServiceScopeFactory>());
        var source = Path.Combine(fixture.DirectoryPath, "template.docx"); StoragePathAdoptionTests.WriteDocument(source, true);
        var first = await service.ImportAsync(null, "模板", "代理", "1.0.0", source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(first.Template.TemplateId, "模板", "代理", "1.0.0", source));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportAsync(first.Template.TemplateId, "模板", "代理", "1.0.1", source, cancel.Token));
        await using var context = fixture.OpenDatabase(db); Assert.Single(await context.TemplateVersions.ToArrayAsync()); Assert.Equal(2, await context.DocumentSnapshots.CountAsync());
    }
    [Fact] public async Task MissingMovedAndChangedSourceKeepHistoryAndRequireExactHashRelink()
    {
        using var fixture = new StorageFixture(); var db = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(db);
        using var provider = Services(fixture, db); var service = new TemplateService(new ComparisonFileInspector(), new OpenXmlDocumentParser(), provider.GetRequiredService<IServiceScopeFactory>());
        var source = Path.Combine(fixture.DirectoryPath, "template.docx"); StoragePathAdoptionTests.WriteDocument(source, true);
        var first = await service.ImportAsync(null, "模板", "代理", "1.0.0", source); var version = first.Current!;
        var moved = Path.Combine(fixture.DirectoryPath, "renamed.docx"); File.Move(source, moved);
        Assert.Single(await service.FindRelinkCandidatesAsync(version)); Assert.Equal(version.Source.Sha256, (await service.LoadSnapshotAsync(version.TemplateVersionId)).Metadata.Sha256);
        await service.RelinkAsync(version.TemplateVersionId, moved); Assert.Equal(moved, (await service.GetAsync(first.Template.TemplateId)).Current!.Source.Path);
        StoragePathAdoptionTests.WriteDocument(moved, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RelinkAsync(version.TemplateVersionId, moved));
        Assert.Equal(version.Source.Sha256, (await service.GetAsync(first.Template.TemplateId)).Current!.Source.Sha256);
    }
    [Fact] public async Task DeleteRequiresConfirmationRetainsReferencedSnapshotAndHistory()
    {
        using var fixture = new StorageFixture(); var db = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(db);
        using var provider = Services(fixture, db); var service = new TemplateService(new ComparisonFileInspector(), new OpenXmlDocumentParser(), provider.GetRequiredService<IServiceScopeFactory>());
        var source = Path.Combine(fixture.DirectoryPath, "template.docx"); StoragePathAdoptionTests.WriteDocument(source, true);
        var first = await service.ImportAsync(null, "模板", "代理", "1.0.0", source);
        await using (var context = fixture.OpenDatabase(db))
        {
            var file = first.Current!.Source; var snapshot = await service.LoadSnapshotAsync(first.Current.TemplateVersionId);
            var saved = await new ComparisonRecordStore(context, new JsonDocumentSnapshotSerializer(), new FinalCheck.Comparison.JsonComparisonResultSerializer())
                .SaveAsync(file, file, snapshot, snapshot, StoragePathAdoptionTests.CreateEngine().Compare(snapshot, snapshot));
            var row = await context.TemplateVersions.SingleAsync(); row.SnapshotId = saved.Record.BaselineSnapshotId; await context.SaveChangesAsync();
        }
        Assert.Equal(1, (await service.ReferencesAsync(first.Template.TemplateId)).Comparisons);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(first.Template.TemplateId, false));
        await service.DeleteAsync(first.Template.TemplateId, true); Assert.Empty(await service.ListAsync());
        Assert.True((await service.GetAsync(first.Template.TemplateId)).Template.IsDeleted);
        Assert.NotNull(await service.LoadSnapshotAsync(first.Current!.TemplateVersionId)); Assert.True(File.Exists(source));
    }
    [Fact] public async Task Schema4UpgradeRetainsAllExistingPayloadsAndIsRecognized()
    {
        using var fixture = new StorageFixture(); var db = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(db, "20260912234643_AddIndependentComparisonRecords");
        await using var context = fixture.OpenDatabase(db); var original = await context.DocumentSnapshots.AsNoTracking().SingleAsync();
        await context.Database.MigrateAsync(); Assert.Equal(original.Payload, (await context.DocumentSnapshots.AsNoTracking().SingleAsync()).Payload);
        await new SqliteDataRootDatabaseInspector().ValidateAsync(db); Assert.Empty(await context.Templates.ToArrayAsync());
    }
    [Fact] public async Task StorageMigrationPreservesTemplateVersionsAndExcludesOriginalInsideRoot()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); var source = Path.Combine(env.Source, "template.docx"); StoragePathAdoptionTests.WriteDocument(source, true);
        await using (var db = env.Factory.CreateDbContext())
        {
            var file = await new ComparisonFileInspector().InspectAsync(source); var snapshot = await new OpenXmlDocumentParser().ParseFileAsync(source);
            await new TemplateStore(db, new DocumentSnapshotStore(db, new JsonDocumentSnapshotSerializer())).AddVersionAsync(null, "模板", "代理", "1.0.0", file, snapshot);
        }
        Assert.Equal(Core.Storage.StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        await using var target = env.Factory.CreateDbContext(); var retained = await target.TemplateVersions.SingleAsync(); Assert.Equal(source, retained.FilePath);
        Assert.True(File.Exists(source)); Assert.False(File.Exists(Path.Combine(env.Target, "template.docx")));
    }
}
