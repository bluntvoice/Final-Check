using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Comparison;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Formatting;
using FinalCheck.Desktop;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Data.Tests;

public sealed class FormatRestoreWorkspaceTests
{
    [Fact] public async Task AnalyzeResolvesVersionIgnoresDisplayPolicyAndDoesNotWriteOrRequireBaselineFile()
    {
        await using var env = await Environment.CreateAsync();
        int snapshotCount;
        await using (var before = env.Provider.CreateAsyncScope()) snapshotCount = await before.ServiceProvider.GetRequiredService<FinalCheckDbContext>().DocumentSnapshots.CountAsync();
        var bytes = File.ReadAllBytes(env.Current); File.Delete(env.Baseline);
        var first = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        var second = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        Assert.Equal(FormatRestoreAnalysisStatus.Ready, first.Status); Assert.Equal(env.Version, first.ContractVersionId);
        Assert.NotEmpty(first.Plan!.RestoreItems); Assert.Equal(first.Plan.PlanId, second.Plan!.PlanId);
        Assert.Equal(bytes, File.ReadAllBytes(env.Current)); Assert.False(Directory.Exists(Path.Combine(env.Root, "WorkingCopies")));
        await using var scope = env.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FinalCheckDbContext>();
        Assert.Empty(await db.FormatRestoreOperations.ToArrayAsync()); Assert.Empty(await db.RestoredWorkingCopies.ToArrayAsync());
        Assert.Equal(snapshotCount, await db.DocumentSnapshots.CountAsync()); Assert.Single(await db.ComparisonRecords.ToArrayAsync());
        Assert.True(first.Comparison!.Record.IgnoreRules.AllFormatting);
    }
    [Fact] public async Task RelinkedSourceIsResolvedFromVersionWithoutMutatingFrozenRecord()
    {
        await using var env = await Environment.CreateAsync(); var moved = Path.Combine(env.Fixture.DirectoryPath, "中文 moved.docx");
        File.Move(env.Current, moved);
        await using (var scope = env.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<FinalCheckDbContext>().ContractVersions.Where(x => x.Id == env.Version)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FilePath, moved));
        var analysis = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        Assert.Equal(FormatRestoreAnalysisStatus.Ready, analysis.Status); Assert.Equal(moved, analysis.SourcePath);
        Assert.Equal(env.Current, analysis.Comparison!.Record.CurrentFile.Path);
    }
    [Fact] public async Task OriginalChangeBlocksPlanAndDoesNotMakeWorkingDirectory()
    {
        await using var env = await Environment.CreateAsync(); StoragePathAdoptionTests.WriteDocument(env.Current, true);
        var analysis = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        Assert.Equal(FormatRestoreAnalysisStatus.SourceChanged, analysis.Status); Assert.Null(analysis.Plan);
        Assert.False(Directory.Exists(Path.Combine(env.Root, "WorkingCopies")));
    }
    [Fact] public async Task ExistingWorkingCopyGetsFreshMappingAndExternalEditBlocksWithoutOverwrite()
    {
        await using var env = await Environment.CreateAsync();
        var initial = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        FormatRestoreResult restored;
        await using (var scope = env.Provider.CreateAsyncScope())
            restored = await scope.ServiceProvider.GetRequiredService<IFormatRestoreWorkingCopyService>().ExecuteAsync(env.Version, env.Current, initial.Plan!);
        Assert.Equal(FormatRestoreResultStatus.Completed, restored.Status);
        var analysis = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        Assert.Equal(FormatRestoreAnalysisStatus.Ready, analysis.Status); Assert.Empty(analysis.Plan!.RestoreItems);
        Assert.Equal(restored.WorkingCopy!.Sha256, analysis.Plan.SourceSha256);
        using (var document = WordprocessingDocument.Open(restored.WorkingCopy.WorkingPath, true))
        { var main = document.MainDocumentPart!; main.Document!.Descendants<Text>().First().Text = "外部新增文字"; main.Document.Save(); }
        var bytes = File.ReadAllBytes(restored.WorkingCopy.WorkingPath);
        var blocked = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        Assert.Equal(FormatRestoreAnalysisStatus.WorkingCopyChanged, blocked.Status); Assert.Null(blocked.Plan);
        Assert.Equal(bytes, File.ReadAllBytes(restored.WorkingCopy.WorkingPath));
    }
    [Fact] public async Task PendingJournalRequiresRecoveryWithoutWritingRecoveryDuringAnalysis()
    {
        await using var env = await Environment.CreateAsync(); var first = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        await using (var scope = env.Provider.CreateAsyncScope())
        {
            var restored = await scope.ServiceProvider.GetRequiredService<IFormatRestoreWorkingCopyService>().ExecuteAsync(env.Version, env.Current, first.Plan!);
            await scope.ServiceProvider.GetRequiredService<IFormatRestoreStore>().SavePreparedAsync(restored.Operation! with
            { OperationId = Guid.NewGuid(), Status = FormatRestoreOperationStatus.Prepared });
        }
        var blocked = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        Assert.Equal(FormatRestoreAnalysisStatus.RecoveryRequired, blocked.Status); Assert.Null(blocked.Plan);
        await using var check = env.Provider.CreateAsyncScope();
        Assert.Single(await check.ServiceProvider.GetRequiredService<IFormatRestoreStore>().LoadPendingAsync(env.Version));
    }
    [Fact] public async Task LegacyUnlinkedHistoryCannotGuessAWorkingCopyIdentity()
    {
        await using var env = await Environment.CreateAsync();
        await using (var scope = env.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<FinalCheckDbContext>().ProjectComparisons.ExecuteDeleteAsync();
        var blocked = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        Assert.Equal(FormatRestoreAnalysisStatus.Unavailable, blocked.Status); Assert.Null(blocked.ContractVersionId);
    }
    [Fact] public async Task PartialSnapshotAndMissingRecordDoNotProducePlan()
    {
        await using var env = await Environment.CreateAsync();
        await using (var scope = env.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinalCheckDbContext>();
            var row = await db.DocumentSnapshots.SingleAsync(x => x.Id == env.Outcome.Record.CurrentSnapshotId);
            row.Payload = new JsonDocumentSnapshotSerializer().Serialize(env.Outcome.Current with { ParseStatus = Core.Documents.DocumentParseStatus.Partial });
            await db.SaveChangesAsync();
        }
        var partial = await env.Analysis.AnalyzeAsync(env.Outcome.Record.RecordId);
        var missing = await env.Analysis.AnalyzeAsync(Guid.NewGuid());
        Assert.Equal(FormatRestoreAnalysisStatus.Unavailable, partial.Status); Assert.Null(partial.Plan);
        Assert.Equal(FormatRestoreAnalysisStatus.Unavailable, missing.Status); Assert.Null(missing.Plan);
    }

    private sealed class Environment : IAsyncDisposable
    {
        public StorageFixture Fixture { get; } = new();
        public ServiceProvider Provider { get; private set; } = null!;
        public FormatRestoreWorkspaceService Analysis { get; private set; } = null!;
        public ComparisonWorkflowResult Outcome { get; private set; } = null!;
        public Guid Version { get; private set; }
        public string Root { get; private set; } = "";
        public string Baseline => Path.Combine(Fixture.DirectoryPath, "plan-baseline.docx");
        public string Current => Path.Combine(Fixture.DirectoryPath, "plan-current.docx");
        public static async Task<Environment> CreateAsync()
        {
            var env = new Environment(); var resolved = await env.Fixture.Resolver.ResolveAsync(); env.Root = resolved.Provider.CurrentDataRoot;
            await env.Fixture.CreateDatabaseAsync(resolved.Provider.DatabasePath);
            await env.Fixture.Resolver.MarkDatabaseInitializedAsync();
            StoragePathAdoptionTests.WriteDocument(env.Baseline, true); StoragePathAdoptionTests.WriteDocument(env.Current, false);
            var coordinator = new StorageMaintenanceCoordinator(resolved.Provider, env.Fixture.Paths, env.Fixture.Bootstrap, env.Fixture.Resolver);
            var services = new ServiceCollection(); var factory = new DataRootDbContextFactory(coordinator, coordinator);
            services.AddSingleton(factory); services.AddScoped(p => p.GetRequiredService<DataRootDbContextFactory>().CreateDbContext());
            services.AddScoped<IAppDataPathProvider>(p => new PlatformAppDataPathProvider(p.GetRequiredService<FinalCheckDbContext>().ManagedPaths));
            services.AddSingleton<IDocumentParser, OpenXmlDocumentParser>();
            services.AddSingleton<IDocumentSnapshotSerializer, JsonDocumentSnapshotSerializer>();
            services.AddSingleton<IComparisonResultSerializer, JsonComparisonResultSerializer>();
            services.AddSingleton<IFormatRestoreRenderer, OpenXmlFormatRestoreRenderer>();
            services.AddSingleton<IWorkingCopyComparisonService>(new WorkingCopyComparisonService(StoragePathAdoptionTests.CreateEngine()));
            services.AddScoped<IComparisonRecordStore, ComparisonRecordStore>(); services.AddScoped<IFormatRestoreStore, FormatRestoreStore>();
            services.AddScoped<IFormatRestoreWorkingCopyService, FormatRestoreWorkingCopyService>();
            env.Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            env.Analysis = new(env.Provider.GetRequiredService<IServiceScopeFactory>(), new SnapshotFormatRestorePlanner(), env.Provider.GetRequiredService<IWorkingCopyComparisonService>());
            var workflow = new ComparisonWorkflowService(new ComparisonFileInspector(), new OpenXmlDocumentParser(), StoragePathAdoptionTests.CreateEngine(), env.Provider.GetRequiredService<IServiceScopeFactory>());
            var inspector = new ComparisonFileInspector();
            env.Outcome = await workflow.ExecuteAsync(await workflow.ValidateAsync(await inspector.InspectAsync(env.Baseline), await inspector.InspectAsync(env.Current)), ignoreRules: new(AllFormatting: true));
            await using var scope = env.Provider.CreateAsyncScope();
            env.Version = (await scope.ServiceProvider.GetRequiredService<FinalCheckDbContext>().ProjectComparisons.SingleAsync()).CurrentVersionId;
            return env;
        }
        public async ValueTask DisposeAsync() { await Provider.DisposeAsync(); Fixture.Dispose(); }
    }
}
