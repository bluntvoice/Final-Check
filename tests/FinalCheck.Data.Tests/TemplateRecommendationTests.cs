using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Comparison;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Management;
using FinalCheck.Core.Storage;
using FinalCheck.Data;
using FinalCheck.Desktop;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data.Tests;

public sealed class TemplateRecommendationTests
{
    private static void WriteContract(string path, string topic, string term)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart(); main.Document = new Document(new Body());
        for (var i = 1; i <= 12; i++)
            main.Document.Body!.Append(new Paragraph(new Run(new Text($"第{i}条 {topic}，合同双方应按约定完成{topic}。履行期限为{term}日，发生争议时以双方书面记录为准。"))));
        main.Document.Save();
    }
    private static ServiceProvider Services(MigrationEnvironment env)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => env.Factory.CreateDbContext());
        services.AddSingleton<IDocumentSnapshotSerializer, JsonDocumentSnapshotSerializer>();
        services.AddSingleton<IComparisonResultSerializer, JsonComparisonResultSerializer>();
        services.AddScoped<DocumentSnapshotStore>(); services.AddScoped<ITemplateStore, TemplateStore>();
        services.AddScoped<IComparisonRecordStore, ComparisonRecordStore>();
        return services.BuildServiceProvider();
    }
    private static async Task<TemplateDetails> AddAsync(ServiceProvider provider, string path, string name,
        string version, Guid? templateId = null)
    {
        var inspector = new ComparisonFileInspector();
        var file = await inspector.InspectAsync(path); var snapshot = await new OpenXmlDocumentParser().ParseFileAsync(path);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITemplateStore>()
            .AddVersionAsync(templateId, name, "合同", version, file, snapshot);
    }
    [Fact] public async Task RecommenderUsesFrozenStructureAndContentForUniqueMultipleCurrentAndNoMatch()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); using var provider = Services(env);
        var templatePath = Path.Combine(env.Fixture.DirectoryPath, "base-a.docx");
        var currentPath = Path.Combine(env.Fixture.DirectoryPath, "random-current.docx");
        var secondPath = Path.Combine(env.Fixture.DirectoryPath, "base-b.docx");
        var thirdPath = Path.Combine(env.Fixture.DirectoryPath, "base-c.docx");
        var unrelatedPath = Path.Combine(env.Fixture.DirectoryPath, "unrelated.docx");
        WriteContract(templatePath, "运输交付与货物验收", "30");
        WriteContract(currentPath, "运输交付与货物验收", "60");
        WriteContract(secondPath, "运输交付与货物验收", "45");
        WriteContract(thirdPath, "运输交付与货物验收", "90");
        WriteContract(unrelatedPath, "知识产权许可及代码托管", "180");
        var details = await AddAsync(provider, templatePath, "运输协议", "1.0");
        var inspector = new ComparisonFileInspector();
        var recommender = new TemplateRecommendationService(inspector, new OpenXmlDocumentParser(), provider.GetRequiredService<IServiceScopeFactory>());
        var current = await inspector.InspectAsync(currentPath);
        var unique = await recommender.RecommendAsync(current);
        Assert.Equal(TemplateRecommendationKind.Unique, unique.Kind);
        Assert.Equal(details.Template.TemplateId, Assert.Single(unique.Candidates).TemplateId);
        await using (var before = env.Factory.CreateDbContext()) Assert.Empty(await before.ComparisonRecords.ToArrayAsync());

        var second = await AddAsync(provider, secondPath, "运输协议", "1.1", details.Template.TemplateId);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ITemplateStore>().SetCurrentAsync(details.Template.TemplateId,
                second.Versions.Single(x => x.Version == "1.1").TemplateVersionId);
        var multiple = await recommender.RecommendAsync(current);
        Assert.Equal(TemplateRecommendationKind.Multiple, multiple.Kind);
        Assert.Equal(2, multiple.Candidates.Count);
        Assert.True(multiple.Candidates.Single(x => x.IsCurrent).Score > multiple.Candidates.Single(x => !x.IsCurrent).Score);
        await AddAsync(provider, thirdPath, "综合运输服务协议", "1.0");
        var topThree = await recommender.RecommendAsync(current);
        Assert.Equal(TemplateRecommendationKind.Multiple, topThree.Kind);
        Assert.Equal(3, topThree.Candidates.Count);
        File.Delete(templatePath); File.Delete(secondPath); File.Delete(thirdPath);
        Assert.Equal(3, (await recommender.RecommendAsync(current)).Candidates.Count);
        var frozenCurrent = await new OpenXmlDocumentParser().ParseFileAsync(currentPath);
        Assert.Equal(3, (await recommender.RecommendSnapshotAsync(frozenCurrent, current.Name)).Candidates.Count);
        var workflow = new ComparisonWorkflowService(inspector, new OpenXmlDocumentParser(),
            StoragePathAdoptionTests.CreateEngine(), provider.GetRequiredService<IServiceScopeFactory>());
        var selectedVersion = multiple.Candidates[0].TemplateVersionId;
        var compared = await workflow.ExecuteTemplateAsync(current, selectedVersion);
        Assert.NotNull(await workflow.LoadAsync(compared.Record.RecordId));
        await using (var db = env.Factory.CreateDbContext())
        {
            var project = Assert.Single(await db.Projects.AsNoTracking().ToArrayAsync());
            Assert.Equal(details.Template.TemplateId, project.BoundTemplateId);
            Assert.Equal(selectedVersion, project.BoundTemplateVersionId);
            var numbers = await db.ContractVersions.AsNoTracking().OrderBy(x => x.VersionNumber)
                .Select(x => x.VersionNumber).ToArrayAsync();
            Assert.Equal([1, 2], numbers);
            var link = Assert.Single(await db.ProjectComparisons.AsNoTracking().ToArrayAsync());
            Assert.Equal((int)ProjectBaselineType.Template, link.BaselineType);
            Assert.Equal(selectedVersion, link.TemplateBaselineVersionId);
        }
        var none = await recommender.RecommendAsync(await inspector.InspectAsync(unrelatedPath));
        Assert.Equal(TemplateRecommendationKind.None, none.Kind);
        Assert.Empty(none.Candidates);
        Assert.Equal(StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        Assert.NotNull(await workflow.LoadAsync(compared.Record.RecordId));
    }
}
