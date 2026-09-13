using FinalCheck.Core.Management;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Comparisons;
using FinalCheck.Data;
using FinalCheck.Documents;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Xunit.Abstractions;

namespace FinalCheck.Data.Tests;

public sealed class ProjectTests(ITestOutputHelper output)
{
    internal static async Task<TemplateDetails> TemplateAsync(FinalCheckDbContext db, string name)
    {
        var file = new ComparisonFile(Path.GetFullPath(name + ".docx"), name + ".docx", 1, DateTimeOffset.UtcNow, new('A', 64));
        var snapshot = DocumentSnapshot.Empty with { Metadata = DocumentMetadataSnapshot.Empty with { Sha256 = file.Sha256 } };
        return await new TemplateStore(db, new DocumentSnapshotStore(db, new JsonDocumentSnapshotSerializer())).AddVersionAsync(null, name, "运输", "1.0.0", file, snapshot);
    }
    [Fact] public async Task CreateEditBindSwitchUnbindAndInheritTypeWithoutGuessingCounterparty()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(database);
        await using var db = fixture.OpenDatabase(database); var a = await TemplateAsync(db, "模板A"); var b = await TemplateAsync(db, "模板B"); var store = new ProjectStore(db);
        var project = await store.SaveAsync(null, new("项目", "", "", a.Template.TemplateId, a.Current!.TemplateVersionId, "", [], ""));
        Assert.Equal("", project.Counterparty); Assert.Equal("运输", project.ContractType); Assert.Null(project.CurrentBaselineVersionId);
        project = await store.SaveAsync(project.ProjectId, new("改名", "用户填写对方", "自定义类型", a.Template.TemplateId, a.Current.TemplateVersionId, "", [], "备注"));
        Assert.Equal(a.Template.TemplateId, project.BoundTemplateId); Assert.Equal("自定义类型", project.ContractType);
        project = await store.SaveAsync(project.ProjectId, new("改名", "用户填写对方", "自定义类型", b.Template.TemplateId, b.Current!.TemplateVersionId, "", [], "备注"));
        Assert.Equal(b.Template.TemplateId, project.BoundTemplateId); Assert.Equal("自定义类型", project.ContractType);
        project = await store.SaveAsync(project.ProjectId, new("改名", "用户填写对方", "自定义类型", null, null, "", [], "备注")); Assert.Null(project.BoundTemplateVersionId);
        await using var reopened = fixture.OpenDatabase(database); var loaded = await new ProjectStore(reopened).GetAsync(project.ProjectId);
        Assert.Equal(project.ProjectId, loaded.ProjectId); Assert.Equal(project.ProjectName, loaded.ProjectName); Assert.Equal(project.Counterparty, loaded.Counterparty); Assert.Equal(project.Notes, loaded.Notes); Assert.Null(loaded.BoundTemplateId);
    }
    [Fact] public async Task FolderTagsSearchFiltersAndPagingReadMetadataWithoutSnapshots()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(database);
        await using var db = fixture.OpenDatabase(database); var template = await TemplateAsync(db, "模板"); var store = new ProjectStore(db);
        var first = await store.SaveAsync(null, new("代理-甲", "甲", "代理", template.Template.TemplateId, template.Current!.TemplateVersionId, "客户", [new("重要", "#2874A6")], ""));
        await store.SaveAsync(null, new("运输-乙", "乙", "运输", null, null, "客户", [new("重要客户", "#148F77")], ""));
        Assert.Single(await store.ListAsync(new(Search: "代理", TemplateId: template.Template.TemplateId, ContractType: "代理", UpdatedSince: DateTimeOffset.UtcNow.AddDays(-1), FolderId: first.FolderId, Tag: "重要")));
        Assert.Single(await store.ListAsync(new(Tag: "重要"))); Assert.Single(await store.FoldersAsync());
        var newest = (await store.ListAsync(new(Limit: 1)))[0]; var older = (await store.ListAsync(new(Offset: 1, Limit: 1)))[0]; Assert.True(newest.Project.UpdatedAt >= older.Project.UpdatedAt);
        await db.DocumentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(x => x.Payload, new byte[] { 0 }));
        Assert.Equal(2, (await store.ListAsync(new())).Count); Assert.Empty(await store.ListAsync(new(UpdatedSince: DateTimeOffset.UtcNow.AddDays(1))));
    }
    [Fact] public async Task InvalidBindingDisabledTemplateAndInvalidTagsFailWithoutChangingProject()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(database);
        await using var db = fixture.OpenDatabase(database); var template = await TemplateAsync(db, "模板"); var store = new ProjectStore(db); var templates = new TemplateStore(db, new DocumentSnapshotStore(db, new JsonDocumentSnapshotSerializer()));
        var edit = new ProjectEdit("项目", "对方", "代理", template.Template.TemplateId, template.Current!.TemplateVersionId, "", [], ""); var project = await store.SaveAsync(null, edit);
        Assert.Contains("项目", (await templates.ReferencesAsync(template.Template.TemplateId)).Projects);
        await templates.UpdateAsync(template.Template.TemplateId, "模板", "代理", "", false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(null, edit));
        await store.SaveAsync(project.ProjectId, edit with { Notes = "保留已有停用绑定" });
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(project.ProjectId, edit with { TemplateVersionId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(project.ProjectId, edit with { Tags = [new("标签", "invalid")] }));
        Assert.Equal(template.Current.TemplateVersionId, (await store.GetAsync(project.ProjectId)).BoundTemplateVersionId); Assert.Single(await db.Projects.ToArrayAsync());
    }
    [Fact] public async Task Schema5UpgradeAndStorageMigrationPreserveProjectBindingsAndTags()
    {
        using (var fixture = new StorageFixture())
        {
            var database = Path.Combine(fixture.DirectoryPath, "legacy", "finalcheck.db"); await fixture.CreateDatabaseAsync(database, "20260913054718_AddTemplateCenter");
            await using var db = fixture.OpenDatabase(database); var old = await db.DocumentSnapshots.AsNoTracking().SingleAsync(); await db.Database.MigrateAsync();
            Assert.Equal(old.Payload, (await db.DocumentSnapshots.AsNoTracking().SingleAsync()).Payload); await new SqliteDataRootDatabaseInspector().ValidateAsync(database);
        }
        await using var env = await MigrationEnvironment.CreateAsync(); Guid projectId;
        await using (var db = env.Factory.CreateDbContext())
        {
            var template = await TemplateAsync(db, "模板"); var project = await new ProjectStore(db).SaveAsync(null, new("迁移项目", "对方", "", template.Template.TemplateId, template.Current!.TemplateVersionId, "客户", [new("迁移", "#148F77")], "保留")); projectId = project.ProjectId;
        }
        Assert.Equal(Core.Storage.StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        await using var target = env.Factory.CreateDbContext(); var retained = await new ProjectStore(target).GetAsync(projectId); Assert.Equal("迁移", retained.Tags[0].Name); Assert.NotNull(retained.BoundTemplateVersionId);
    }
    [Fact] public async Task HundredProjectMetadataPageRemainsBounded()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "data", "finalcheck.db"); await fixture.CreateDatabaseAsync(database);
        await using var db = fixture.OpenDatabase(database); var store = new ProjectStore(db);
        for (var i = 0; i < 100; i++) await store.SaveAsync(null, new($"合同项目{i:D3}", "对方", "代理", null, null, "", [], ""));
        db.ChangeTracker.Clear(); var watch = Stopwatch.StartNew(); var page = await store.ListAsync(new(Limit: 20)); watch.Stop();
        Assert.Equal(20, page.Count); Assert.Equal(100, (await store.ListAsync(new(Limit: 100))).Count); Assert.Empty(await store.ListAsync(new(Offset: 100)));
        output.WriteLine($"100 projects / first metadata page (20): {watch.Elapsed.TotalMilliseconds:F1} ms.");
    }
}
