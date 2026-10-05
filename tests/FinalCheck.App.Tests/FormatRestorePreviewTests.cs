using FinalCheck.App.ViewModels;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;

namespace FinalCheck.App.Tests;

public sealed class FormatRestorePreviewTests
{
    internal static FormatRestoreAnalysis Ready()
    {
        var comparison = ComparisonResultsTests.Result(ComparisonResultsTests.Change());
        FormatRestoreItem Item(string id, FormatRestoreCategory category, bool eligible = true) => new(id, "p0", "p0", DocumentNodeKind.Paragraph,
            new("p0", null, DocumentNodeKind.Paragraph, "body/p[0]", "word/document.xml", 0),
            new(Character: CharacterFormatSnapshot.Empty with { FontSizeHalfPoints = 20 }),
            new(Character: CharacterFormatSnapshot.Empty with { FontSizeHalfPoints = 24 }),
            ["Character.FontSizeHalfPoints"], category, eligible ? FormatRestoreEligibility.Eligible : FormatRestoreEligibility.NeedsReview,
            null, eligible ? 1 : 0.5, null, eligible ? [] : [new("LowConfidenceMapping", "p0", "technical")]);
        var items = new[] { Item("char", FormatRestoreCategory.Character), Item("table", FormatRestoreCategory.Table),
            Item("cell", FormatRestoreCategory.Cell), Item("low", FormatRestoreCategory.Paragraph, false) };
        var plan = new FormatRestorePlan(1, "plan", "source", "baseline", "current", "sha", "mapping", DateTimeOffset.UnixEpoch,
            new(), items, [new("UnsupportedTableStyle", "table", "technical")], FormatRestorePlanStatus.NeedsReview);
        return new(FormatRestoreAnalysisStatus.Ready, Guid.NewGuid(), comparison, plan, null, "已生成") { SourcePath = "current.docx" };
    }
    internal sealed class Service : IFormatRestoreWorkspaceService
    {
        public FormatRestoreAnalysis Result { get; set; } = Ready();
        public bool Fail { get; set; }
        public Guid RequestedId { get; private set; }
        public Task<FormatRestoreAnalysis> AnalyzeAsync(Guid id, CancellationToken cancellationToken = default)
        { RequestedId = id; return Fail ? Task.FromException<FormatRestoreAnalysis>(new IOException("secret-path")) : Task.FromResult(Result); }
    }
    [Fact] public async Task AllAndTableSelectionsExcludeLowConfidenceAndIncludeCells()
    {
        var vm = new FormatRestoreViewModel(new Service(), Guid.NewGuid()); await vm.AnalyzeCommand.ExecuteAsync(null);
        Assert.Empty(vm.Selection.SelectedItemIds!); // Analyzing does not imply consent to restoration.
        vm.SelectAllCommand.Execute(null); Assert.Equal(3, vm.Selection.SelectedItemIds!.Count);
        vm.Items[3].Selected = true; Assert.False(vm.Items[3].Selected);
        vm.Category = "表格"; vm.SelectCategoryCommand.Execute(null);
        Assert.Collection(vm.Selection.SelectedItemIds!, id => Assert.Equal("table", id), id => Assert.Equal("cell", id));
        vm.Items[1].Selected = false; Assert.Equal("cell", Assert.Single(vm.Selection.SelectedItemIds!));
        vm.ClearSelectionCommand.Execute(null); Assert.Empty(vm.Selection.SelectedItemIds!);
    }
    [Fact] public async Task PreviewShowsActualCurrentAndTargetUnitsAndHonestDiagnostics()
    {
        var vm = new FormatRestoreViewModel(new Service(), Guid.NewGuid()); await vm.AnalyzeCommand.ExecuteAsync(null);
        var property = Assert.Single(vm.Items[0].Properties);
        Assert.Equal("字号", property.Name); Assert.Equal("10 磅", property.Current); Assert.Equal("12 磅", property.Target);
        Assert.Contains("需审阅", vm.Items[3].EligibilityLabel); Assert.Contains("保留当前格式", vm.Items[3].Diagnostics);
        Assert.Contains("暂不支持", vm.Diagnostics); Assert.DoesNotContain("technical", vm.Diagnostics);
    }
    [Fact] public async Task FailedReanalysisClearsOldPlanAndSelectionWithoutLeakingError()
    {
        var service = new Service(); var vm = new FormatRestoreViewModel(service, Guid.NewGuid());
        await vm.AnalyzeCommand.ExecuteAsync(null); vm.SelectAllCommand.Execute(null);
        service.Fail = true; await vm.AnalyzeCommand.ExecuteAsync(null);
        Assert.False(vm.HasPlan); Assert.False(vm.CanSelect); Assert.Empty(vm.Items); Assert.Empty(vm.Selection.SelectedItemIds!);
        Assert.Contains("加载失败", vm.Message); Assert.DoesNotContain("secret", vm.Message); Assert.False(vm.IsAnalyzing);
    }
    [Fact] public async Task WorkspaceUsesSavedRecordAndKeepsComparisonReviewAndFacts()
    {
        var outcome = ComparisonResultsTests.Result(ComparisonResultsTests.Change()); var service = new Service();
        var vm = new ComparisonResultsViewModel(outcome, restore: service);
        await vm.OpenFormatRestoreCommand.ExecuteAsync(null);
        Assert.True(vm.FormatRestoreMode); Assert.Equal(outcome.Record.RecordId, service.RequestedId);
        vm.Restore.SelectAllCommand.Execute(null); vm.CloseFormatRestoreCommand.Execute(null);
        Assert.True(vm.ComparisonMode); Assert.Same(outcome, vm.Outcome);
        Assert.Equal(Core.Comparisons.ComparisonReviewState.Unresolved, vm.Changes[0].ReviewState);
    }
    [Theory] [InlineData(FormatRestoreAnalysisStatus.SourceChanged)] [InlineData(FormatRestoreAnalysisStatus.WorkingCopyChanged)]
    [InlineData(FormatRestoreAnalysisStatus.RecoveryRequired)]
    public async Task BlockedAnalysisHasNoSelectablePlan(FormatRestoreAnalysisStatus status)
    {
        var service = new Service { Result = new(status, null, null, null, null, "请检查当前文件") };
        var vm = new FormatRestoreViewModel(service, Guid.NewGuid()); await vm.AnalyzeCommand.ExecuteAsync(null);
        vm.SelectAllCommand.Execute(null); Assert.False(vm.HasPlan); Assert.False(vm.CanSelect); Assert.Empty(vm.Items);
        Assert.Equal("请检查当前文件", vm.Message);
    }
}
