using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Storage;
using FinalCheck.Desktop;
using FinalCheck.Documents;
using FinalCheck.Comparison;
using FinalCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Data.Tests;

public sealed class ComparisonWorkflowTests
{
    internal static ServiceProvider Services(MigrationEnvironment env)
    {
        var services = new ServiceCollection();
        services.AddSingleton(env.Factory); services.AddScoped(p => p.GetRequiredService<DataRootDbContextFactory>().CreateDbContext());
        services.AddSingleton<IDocumentSnapshotSerializer, JsonDocumentSnapshotSerializer>();
        services.AddSingleton<IComparisonResultSerializer, JsonComparisonResultSerializer>();
        services.AddScoped<IComparisonRecordStore, ComparisonRecordStore>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
    private static ComparisonWorkflowService Workflow(ServiceProvider provider) => new(new ComparisonFileInspector(), new OpenXmlDocumentParser(), StoragePathAdoptionTests.CreateEngine(), provider.GetRequiredService<IServiceScopeFactory>());
    internal static void WriteDocument(string path, string value, bool partial = false, int count = 12)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart(); main.Document = new Document(new Body());
        for (var i = 0; i < count; i++) main.Document.Body!.Append(new Paragraph(new Run(new Text($"第{i + 1}条 付款期限为{value}日，双方应及时履行本条约定并核实付款信息。"))));
        if (partial) main.Document.Body!.Append(new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text("可读表格"))),
            new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text("嵌套内容"))))))))));
        main.Document.Save();
    }
    [Fact] public async Task RealWorkflowReportsStagesPersistsFrozenHistoryAndKeepsOriginalsExternal()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); await using var provider = Services(env);
        var left = Path.Combine(env.Fixture.DirectoryPath, "ui-baseline.docx"); var right = Path.Combine(env.Fixture.DirectoryPath, "ui-current.docx");
        WriteDocument(left, "30"); WriteDocument(right, "60");
        var inspector = new ComparisonFileInspector(); var workflow = Workflow(provider);
        var input = await workflow.ValidateAsync(await inspector.InspectAsync(left), await inspector.InspectAsync(right));
        var stages = new StageCapture(); var result = await workflow.ExecuteAsync(input, stages);
        Assert.False(result.IsPartial); Assert.True(result.Result.Statistics.TotalChanges > 0);
        Assert.Contains("正在读取基准文档…", stages.Values); Assert.Contains("正在比较文字…", stages.Values); Assert.Contains("正在保存比对结果…", stages.Values);
        Assert.Equal(input.Current.Sha256, (await inspector.InspectAsync(right)).Sha256);
        Assert.Empty(Directory.EnumerateFiles(env.Source, "ui-*.docx", SearchOption.AllDirectories));
        File.Delete(left); File.Delete(right);
        var loaded = await workflow.LoadAsync(result.Record.RecordId); Assert.NotNull(loaded);
        Assert.Equal(result.Record.ResultId, loaded.Record.ResultId); Assert.Equal(result.Current.Paragraphs[0].DisplayText, loaded.Current.Paragraphs[0].DisplayText);
        Assert.Contains((await workflow.ListAsync()), record => record.RecordId == result.Record.RecordId);
    }
    [Fact] public async Task IdenticalInputsHaveZeroActualChangesAndSamePathIsExplicit()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); await using var provider = Services(env);
        var path = Path.Combine(env.Fixture.DirectoryPath, "same.docx"); WriteDocument(path, "30");
        var file = await new ComparisonFileInspector().InspectAsync(path); var workflow = Workflow(provider);
        var validation = await workflow.ValidateAsync(file, file); Assert.True(validation.SameHash); Assert.True(validation.SamePath);
        Assert.Empty((await workflow.ExecuteAsync(validation)).Result.Changes);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task InvalidAndEncryptedFailWithoutSaving(bool encrypted)
    {
        await using var env = await MigrationEnvironment.CreateAsync(); await using var provider = Services(env);
        var path = Path.Combine(env.Fixture.DirectoryPath, "invalid.docx");
        await File.WriteAllBytesAsync(path, encrypted ? [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1] : [1, 2, 3]);
        var file = await new ComparisonFileInspector().InspectAsync(path); var workflow = Workflow(provider);
        var error = await Assert.ThrowsAsync<DocumentParseException>(() => workflow.ExecuteAsync(awaitInput(file)));
        Assert.Equal(encrypted ? DocumentParseErrorKind.Encrypted : DocumentParseErrorKind.UnsupportedFormat, error.ErrorKind);
        Assert.Empty(await workflow.ListAsync());
        static ComparisonInputValidation awaitInput(ComparisonFile value) => new(value, value, true, true, false);
    }
    [Fact] public async Task PartialIsPreservedWithDiagnostics()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); await using var provider = Services(env);
        var path = Path.Combine(env.Fixture.DirectoryPath, "partial.docx"); WriteDocument(path, "30", true);
        var file = await new ComparisonFileInspector().InspectAsync(path);
        var result = await Workflow(provider).ExecuteAsync(new(file, file, true, true, false));
        Assert.True(result.IsPartial); Assert.Contains(result.Current.ParseDiagnostics, d => d.Code == "UnsupportedNestedTable");
        Assert.True((await Workflow(provider).LoadAsync(result.Record.RecordId))!.IsPartial);
    }
    [Fact] public async Task CancellationDuringComparisonDoesNotPersistAndCanRetry()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); await using var provider = Services(env);
        var path = Path.Combine(env.Fixture.DirectoryPath, "cancel.docx"); WriteDocument(path, "30");
        var file = await new ComparisonFileInspector().InspectAsync(path); var input = new ComparisonInputValidation(file, file, true, true, false);
        using var cancellation = new CancellationTokenSource(); var workflow = Workflow(provider);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.ExecuteAsync(input, new CancelProgress(cancellation), cancellation.Token));
        Assert.Empty(await workflow.ListAsync()); Assert.NotNull(await workflow.ExecuteAsync(input));
    }
    [Fact] public async Task SelectionChangeRefreshesHashButChangeAfterValidationIsRejected()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); await using var provider = Services(env);
        var path = Path.Combine(env.Fixture.DirectoryPath, "changed.docx"); WriteDocument(path, "30");
        var file = await new ComparisonFileInspector().InspectAsync(path); WriteDocument(path, "60"); var workflow = Workflow(provider);
        var validation = await workflow.ValidateAsync(file, file); Assert.True(validation.Changed); Assert.NotEqual(file.Sha256, validation.Current.Sha256);
        WriteDocument(path, "90"); await Assert.ThrowsAsync<IOException>(() => workflow.ExecuteAsync(validation)); Assert.Empty(await workflow.ListAsync());
    }
    [Fact] public async Task Schema3UpgradeRetainsExistingPayloadAndReadonlyInspectorAcceptsNewSchema()
    {
        using var fixture = new StorageFixture(); var database = Path.Combine(fixture.DirectoryPath, "legacy", "finalcheck.db");
        await fixture.CreateDatabaseAsync(database, "20260912110000_AddFormatRestoreHistory");
        await using var context = fixture.OpenDatabase(database); var old = await context.DocumentSnapshots.AsNoTracking().SingleAsync();
        await context.Database.MigrateAsync(); var retained = await context.DocumentSnapshots.AsNoTracking().SingleAsync();
        Assert.Equal(old.Payload, retained.Payload); Assert.Equal(old.Id, retained.Id);
        Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync()).Count()); await new SqliteDataRootDatabaseInspector().ValidateAsync(database);
    }
    [Fact] public async Task StorageMigrationPreservesRecordsAndExcludesTheirOriginalsEvenInsideOldRoot()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); await using var provider = Services(env);
        var path = Path.Combine(env.Source, "original-ui.docx"); WriteDocument(path, "30");
        var file = await new ComparisonFileInspector().InspectAsync(path); var workflow = Workflow(provider);
        var comparison = await workflow.ExecuteAsync(new(file, file, true, true, false));
        Assert.Equal(StorageMigrationStatus.Completed, (await env.Migration().MigrateAsync(env.Target)).Status);
        Assert.True(File.Exists(path)); Assert.False(File.Exists(Path.Combine(env.Target, "original-ui.docx")));
        var loaded = await workflow.LoadAsync(comparison.Record.RecordId); Assert.Equal(path, loaded!.Record.BaselineFile.Path);
        await using var context = env.Factory.CreateDbContext();
        var usage = await new SqliteStorageUsageReader().ReadAsync(context.ManagedPaths.DatabasePath);
        Assert.Contains(path, usage.OriginalPaths);
    }
    [Fact] public async Task ReviewPersistsAcrossNewScopesWithoutChangingFrozenPayloads()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); await using var provider = Services(env);
        var path = Path.Combine(env.Fixture.DirectoryPath, "review.docx"); var right = Path.Combine(env.Fixture.DirectoryPath, "review-current.docx");
        WriteDocument(path, "30"); WriteDocument(right, "60"); var inspector = new ComparisonFileInspector(); var workflow = Workflow(provider);
        var result = await workflow.ExecuteAsync(await workflow.ValidateAsync(await inspector.InspectAsync(path), await inspector.InspectAsync(right)));
        byte[] snapshot; byte[] comparison;
        await using (var context = env.Factory.CreateDbContext())
        {
            snapshot = (await context.DocumentSnapshots.AsNoTracking().SingleAsync(s => s.Id == result.Record.CurrentSnapshotId)).Payload;
            comparison = (await context.ComparisonResults.AsNoTracking().SingleAsync(s => s.Id == result.Record.ResultId)).Payload;
        }
        var id = result.Result.Changes[0].ChangeId;
        await workflow.UpdateReviewAsync(result.Record.RecordId, [id], ComparisonReviewState.Confirmed);
        await using var fresh = Services(env); var loaded = await Workflow(fresh).LoadAsync(result.Record.RecordId);
        Assert.Equal(ComparisonReviewState.Confirmed, loaded!.Record.ReviewStates[id]);
        await using var check = env.Factory.CreateDbContext();
        Assert.Equal(snapshot, (await check.DocumentSnapshots.AsNoTracking().SingleAsync(s => s.Id == result.Record.CurrentSnapshotId)).Payload);
        Assert.Equal(comparison, (await check.ComparisonResults.AsNoTracking().SingleAsync(s => s.Id == result.Record.ResultId)).Payload);
        Assert.Equal((await inspector.InspectAsync(right)).Sha256, result.Record.CurrentFile.Sha256);
    }
    [Fact] public async Task ConcurrentReviewsMergeDifferentChangeIdsAndRejectInvalidOperations()
    {
        await using var env = await MigrationEnvironment.CreateAsync(); await using var provider = Services(env);
        var left = Path.Combine(env.Fixture.DirectoryPath, "parallel-left.docx"); var right = Path.Combine(env.Fixture.DirectoryPath, "parallel-right.docx");
        WriteDocument(left, "30"); WriteDocument(right, "60"); var inspector = new ComparisonFileInspector(); var workflow = Workflow(provider);
        var result = await workflow.ExecuteAsync(await workflow.ValidateAsync(await inspector.InspectAsync(left), await inspector.InspectAsync(right)));
        var first = result.Result.Changes[0].ChangeId; var second = result.Result.Changes[1].ChangeId;
        await Task.WhenAll(workflow.UpdateReviewAsync(result.Record.RecordId, [first], ComparisonReviewState.Confirmed),
            workflow.UpdateReviewAsync(result.Record.RecordId, [second], ComparisonReviewState.Ignored));
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.UpdateReviewAsync(result.Record.RecordId, ["unknown"], ComparisonReviewState.Confirmed));
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.UpdateReviewAsync(result.Record.RecordId, [first], (ComparisonReviewState)999));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.UpdateReviewAsync(result.Record.RecordId, [first], ComparisonReviewState.Ignored, cancellation.Token));
        var loaded = await workflow.LoadAsync(result.Record.RecordId);
        Assert.Equal(ComparisonReviewState.Confirmed, loaded!.Record.ReviewStates[first]); Assert.Equal(ComparisonReviewState.Ignored, loaded.Record.ReviewStates[second]);
    }
    private sealed class StageCapture : IProgress<string> { public List<string> Values { get; } = []; public void Report(string value) => Values.Add(value); }
    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<string>
    { public void Report(string value) { if (value == "正在比较文字…") cancellation.Cancel(); } }
}
