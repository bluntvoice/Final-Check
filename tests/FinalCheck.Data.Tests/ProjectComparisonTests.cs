using System.Diagnostics;
using FinalCheck.Comparison;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Management;
using FinalCheck.Desktop;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FinalCheck.Data.Tests;
public sealed class ProjectComparisonTests(ITestOutputHelper output)
{
    internal static ServiceProvider Services(MigrationEnvironment env)
    {
        var services = new ServiceCollection(); services.AddScoped(_ => env.Factory.CreateDbContext()); services.AddSingleton<IDocumentSnapshotSerializer, JsonDocumentSnapshotSerializer>();
        services.AddSingleton<IComparisonResultSerializer, JsonComparisonResultSerializer>(); services.AddScoped<DocumentSnapshotStore>(); services.AddScoped<IContractVersionStore, ContractVersionStore>();
        services.AddScoped<IProjectComparisonStore, ProjectComparisonStore>(); services.AddScoped<IComparisonRecordStore, ComparisonRecordStore>(); services.AddScoped<IProjectLifecycleStore, ProjectLifecycleStore>(); return services.BuildServiceProvider();
    }
    internal static async Task<(Guid Project, TemplateDetails Template, IReadOnlyList<ContractVersion> Versions)> ChainAsync(MigrationEnvironment env, ServiceProvider provider)
    {
        var source = Path.Combine(env.Fixture.DirectoryPath, "template.docx"); ComparisonWorkflowTests.WriteDocument(source, "30");
        Guid project; TemplateDetails template;
        await using (var db = env.Factory.CreateDbContext())
        {
            var file = await new ComparisonFileInspector().InspectAsync(source); var snapshot = await new OpenXmlDocumentParser().ParseFileAsync(source);
            template = await new TemplateStore(db, new DocumentSnapshotStore(db, new JsonDocumentSnapshotSerializer())).AddVersionAsync(null, "模板", "代理", "1.0.0", file, snapshot);
            project = (await new ProjectStore(db).SaveAsync(null, new("谈判项目", "脱敏对方", "代理", template.Template.TemplateId, template.Current!.TemplateVersionId, "", [], ""))).ProjectId;
        }
        var requests = new List<VersionImport>(); var contents = new[] { "30", "60", "45", "90" };
        for (var i = 0; i < contents.Length; i++) { var path = Path.Combine(env.Fixture.DirectoryPath, $"version{i}.docx"); ComparisonWorkflowTests.WriteDocument(path, contents[i]); requests.Add(new(path, i % 2 == 0 ? ContractVersionRole.Own : ContractVersionRole.Counterparty, i / 2 + 1, "", true)); }
        var versions = await new ContractVersionService(new ComparisonFileInspector(), new OpenXmlDocumentParser(), provider.GetRequiredService<IServiceScopeFactory>()).ImportAsync(project, requests);
        return (project, template, versions);
    }
    internal static ProjectComparisonService Service(ServiceProvider provider) => new(StoragePathAdoptionTests.CreateEngine(), provider.GetRequiredService<IServiceScopeFactory>());
    [Fact] public async Task OnlyOwnSameProjectCanBecomeBaselineAndNewOwnDoesNotReplaceIt()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); using var provider = Services(env); var chain = await ChainAsync(env, provider); var service = Service(provider);
        await service.SetBaselineAsync(chain.Project, chain.Versions[0].ContractVersionId);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetBaselineAsync(chain.Project, chain.Versions[1].ContractVersionId));
        Guid other; await using (var db = env.Factory.CreateDbContext()) other = await VersionTests.ProjectAsync(db); await Assert.ThrowsAsync<ArgumentException>(() => service.SetBaselineAsync(other, chain.Versions[0].ContractVersionId));
        await using var loaded = env.Factory.CreateDbContext(); Assert.Equal(chain.Versions[0].ContractVersionId, (await new ProjectStore(loaded).GetAsync(chain.Project)).CurrentBaselineVersionId);
        await service.SetBaselineAsync(chain.Project, chain.Versions[2].ContractVersionId); var choices = await service.ChoicesAsync(chain.Project, chain.Versions[3].ContractVersionId);
        Assert.Equal(chain.Versions[2].ContractVersionId, choices.Options.Single(x => x.Type == ProjectBaselineType.Own).VersionId); Assert.Contains("不与上一版对方", choices.Recommendation);
    }
    [Fact] public async Task SameVersionMultipleBaselinesTemplateSwitchAndReviewPreserveIndependentHistory()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); using var provider = Services(env); var chain = await ChainAsync(env, provider); var service = Service(provider); var current = chain.Versions[3];
        await service.SetBaselineAsync(chain.Project, chain.Versions[2].ContractVersionId);
        var own = await service.CompareAsync(new(chain.Project, current.ContractVersionId, ProjectBaselineType.Own, chain.Versions[2].ContractVersionId));
        var template = await service.CompareAsync(new(chain.Project, current.ContractVersionId, ProjectBaselineType.Template, chain.Template.Current!.TemplateVersionId)); Assert.NotEqual(own.Record.RecordId, template.Record.RecordId);
        await using (var db = env.Factory.CreateDbContext())
        {
            var row = await db.ComparisonResults.AsNoTracking().SingleAsync(x => x.Id == own.Record.ResultId); var payload = row.Payload.ToArray();
            var records = new ComparisonRecordStore(db, new JsonDocumentSnapshotSerializer(), new JsonComparisonResultSerializer()); await records.UpdateReviewAsync(own.Record.RecordId, [own.Result.Changes[0].ChangeId], ComparisonReviewState.Confirmed);
            var next = await ProjectTests.TemplateAsync(db, "新模板"); await new ProjectStore(db).SaveAsync(chain.Project, new("改项目", "对方", "代理", next.Template.TemplateId, next.Current!.TemplateVersionId, "", [], ""));
            await service.SetBaselineAsync(chain.Project, chain.Versions[0].ContractVersionId); var frozen = await records.LoadAsync(own.Record.RecordId); Assert.Equal(own.Record.BaselineSnapshotId, frozen!.Record.BaselineSnapshotId); Assert.Equal(payload, (await db.ComparisonResults.AsNoTracking().SingleAsync(x => x.Id == own.Record.ResultId)).Payload);
            Assert.Equal(1, (await new TemplateStore(db, new DocumentSnapshotStore(db, new JsonDocumentSnapshotSerializer())).ReferencesAsync(chain.Template.Template.TemplateId)).Comparisons);
        }
        var history = await service.HistoryAsync(chain.Project, current.ContractVersionId); Assert.Equal(2, history.Count); Assert.Equal(1, history.Single(x => x.RecordId == own.Record.RecordId).Reviewed);
        Assert.Equal(ProjectBaselineType.Template, (await service.ChoicesAsync(chain.Project, current.ContractVersionId)).LastType);
    }
    [Fact] public async Task FrozenSnapshotComparisonSurvivesMissingSourcesAndCancelledSaveDoesNotLeaveOrphans()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); using var provider = Services(env); var chain = await ChainAsync(env, provider); var service = Service(provider);
        foreach (var version in chain.Versions) File.Delete(version.Source.Path); File.Delete(chain.Template.Current!.Source.Path);
        var selection = new ProjectComparisonSelection(chain.Project, chain.Versions[1].ContractVersionId, ProjectBaselineType.Template, chain.Template.Current.TemplateVersionId);
        var saved = await service.CompareAsync(selection); Assert.NotEmpty(saved.Result.Changes);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompareAsync(selection, token: cancel.Token)); Assert.Single(await service.HistoryAsync(chain.Project));
        await using var db = env.Factory.CreateDbContext(); Assert.Single(await db.ProjectComparisons.ToArrayAsync()); Assert.Single(await db.ComparisonRecords.ToArrayAsync());
    }
    [Fact] public async Task ThirtyHistoryRowsArePagedAndStorageMigrationPreservesContexts()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); using var provider = Services(env); var chain = await ChainAsync(env, provider); var service = Service(provider);
        var selection = new ProjectComparisonSelection(chain.Project, chain.Versions[1].ContractVersionId, ProjectBaselineType.Template, chain.Template.Current!.TemplateVersionId);
        for (var i = 0; i < 30; i++) await service.CompareAsync(selection);
        var watch = Stopwatch.StartNew(); var page = await service.HistoryAsync(chain.Project); watch.Stop(); Assert.Equal(20, page.Count); Assert.Equal(10, (await service.HistoryAsync(chain.Project, offset: 20)).Count);
        output.WriteLine($"30 comparisons / 20 history metadata rows: {watch.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.Equal(Core.Storage.StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status); Assert.Equal(30, (await service.HistoryAsync(chain.Project, limit: 100)).Count);
    }
    [Fact] public async Task Schema7UpgradePreservesExistingVersionAndQuickComparePayloads()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "legacy", "finalcheck.db"); await fixture.CreateDatabaseAsync(database, "20260913060404_AddContractVersionsAndRounds");
        await using var db = fixture.OpenDatabase(database); var payload = (await db.DocumentSnapshots.AsNoTracking().SingleAsync()).Payload; await db.Database.MigrateAsync(); await new SqliteDataRootDatabaseInspector().ValidateAsync(database); Assert.Equal(payload, (await db.DocumentSnapshots.AsNoTracking().SingleAsync()).Payload);
    }
}
