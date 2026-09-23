using FinalCheck.App.ViewModels;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.App.Tests;

public sealed class ComparisonReviewTests
{
    private sealed class Workflow(ComparisonWorkflowResult result, bool fail = false) : IComparisonWorkflowService
    {
        public ComparisonRecord Record { get; private set; } = result.Record;
        public Task<ComparisonRecord> UpdateReviewAsync(Guid recordId, IReadOnlyList<string> changeIds, ComparisonReviewState state, CancellationToken cancellationToken = default)
        {
            if (fail) throw new IOException("injected failure");
            var states = Record.ReviewStates.ToDictionary(p => p.Key, p => p.Value); foreach (var id in changeIds) states[id] = state;
            Record = Record with { ReviewStates = states }; return Task.FromResult(Record);
        }
        public Task<ComparisonInputValidation> ValidateAsync(ComparisonFile baseline, ComparisonFile current, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ComparisonRecord> EditReviewAsync(Guid recordId, ComparisonReviewEdit edit, CancellationToken cancellationToken = default)
        {
            if (fail) throw new IOException("injected failure");
            if (edit.ExpectedStates.Any(pair => Record.ReviewStates[pair.Key] != pair.Value)) throw new InvalidOperationException("conflict");
            var states = Record.ReviewStates.ToDictionary(p => p.Key, p => p.Value);
            foreach (var pair in edit.States) states[pair.Key] = pair.Value;
            Record = Record with { ReviewStates = states }; return Task.FromResult(Record);
        }
        public Task<ComparisonWorkflowResult> ExecuteAsync(ComparisonInputValidation input, IProgress<string>? progress = null, ComparisonIgnoreRules? ignoreRules = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ComparisonWorkflowResult> ExecuteTemplateAsync(ComparisonFile current, Guid templateVersionId,
            IProgress<string>? progress = null, ComparisonIgnoreRules? ignoreRules = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ComparisonRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ComparisonRecord>>([Record]);
        public Task<ComparisonWorkflowResult?> LoadAsync(Guid recordId, CancellationToken cancellationToken = default) => Task.FromResult<ComparisonWorkflowResult?>(result with { Record = Record });
    }
    private static ComparisonWorkflowResult Grouped()
    {
        var result = ComparisonResultsTests.Result(ComparisonResultsTests.Change(), ComparisonResultsTests.Change("two"));
        return result with { Result = result.Result with { Groups = [new("group", ComparisonChangeKind.TextReplace, "30", "60", ["one", "two"])] } };
    }
    [Fact] public async Task ItemAndGroupStatusStayInSyncAndReloadUsesSavedStates()
    {
        var result = Grouped(); var workflow = new Workflow(result); var vm = new ComparisonResultsViewModel(result, workflow);
        Assert.Equal("未处理", vm.Entries[0].ReviewLabel);
        await vm.ReviewSelectedCommand.ExecuteAsync("Confirmed"); Assert.Equal("部分已审阅", vm.Entries[0].ReviewLabel);
        vm.Grouped = false; Assert.Equal("已审阅", vm.Entries[0].ReviewLabel); vm.Grouped = true;
        await vm.ReviewGroupCommand.ExecuteAsync("Confirmed"); Assert.Equal("已审阅", vm.Entries[0].ReviewLabel);
        var reloaded = new ComparisonResultsViewModel((await workflow.LoadAsync(result.Record.RecordId))!, workflow);
        Assert.All(reloaded.Changes, c => Assert.Equal(ComparisonReviewState.Confirmed, c.ReviewState));
    }
    [Fact] public async Task IgnoredIsHiddenCanBeShownAndRestoredWithoutRemovingConfirmed()
    {
        var result = ComparisonResultsTests.Result(ComparisonResultsTests.Change(), ComparisonResultsTests.Change("two"));
        var vm = new ComparisonResultsViewModel(result, new Workflow(result));
        await vm.ReviewSelectedCommand.ExecuteAsync("Ignored"); Assert.Single(vm.Entries); Assert.Equal("1 / 2 项", vm.CountLabel);
        vm.StatusFilter = "已忽略"; Assert.Single(vm.Entries); Assert.Equal("one", vm.SelectedChange!.ChangeId);
        await vm.ReviewSelectedCommand.ExecuteAsync("Unresolved"); Assert.Empty(vm.Entries);
        vm.StatusFilter = "全部"; await vm.ReviewSelectedCommand.ExecuteAsync("Confirmed"); Assert.Equal(2, vm.Entries.Count);
        vm.OnlyUnresolvedCommand.Execute(null); Assert.Single(vm.Entries); Assert.Equal("two", vm.SelectedChange!.ChangeId);
    }
    [Fact] public void SearchAndTypeFilterCombineAndShowCorrectUnderlyingCounts()
    {
        var first = ComparisonResultsTests.Change(); var format = ComparisonResultsTests.Change("format", ComparisonChangeKind.CharacterFormatChange) with
        { DifferenceSpans = [], FormatDifference = new(FormatDifferenceScope.Character, "p0", "p0", [new("Bold", "False", "True")]) };
        var vm = new ComparisonResultsViewModel(ComparisonResultsTests.Result(first, format));
        Assert.Equal(string.Empty, vm.EmptyMessage);
        vm.SearchText = "30"; Assert.Equal(2, vm.VisibleCount); vm.TypeFilter = "格式"; Assert.Single(vm.Entries);
        vm.TypeFilter = "文字"; Assert.Equal("one", vm.SelectedChange!.ChangeId); vm.SearchText = "60"; Assert.Single(vm.Entries);
        vm.SearchText = "不存在"; Assert.True(vm.NoVisibleEntries); Assert.Equal("0 / 2 项", vm.CountLabel); Assert.Contains("筛选没有结果", vm.EmptyMessage);
    }
    [Fact] public void SearchIncludesCommentContent()
    {
        var result = ComparisonResultsTests.Result(ComparisonResultsTests.Change() with { CommentIds = ["c"] });
        result = result with { Current = result.Current with { Comments = [new("c", "请核实账户资料", "测试作者", null, null, null, null, "p0", null, true)] } };
        var vm = new ComparisonResultsViewModel(result) { SearchText = "账户资料", TypeFilter = "批注" }; Assert.Single(vm.Entries);
    }
    [Fact] public async Task FailedSaveDoesNotPretendReviewWasPersisted()
    {
        var result = Grouped(); var vm = new ComparisonResultsViewModel(result, new Workflow(result, true));
        await vm.ReviewSelectedCommand.ExecuteAsync("Confirmed"); Assert.All(vm.Changes, c => Assert.Equal(ComparisonReviewState.Unresolved, c.ReviewState));
        Assert.Contains("未保存", vm.ReviewMessage); Assert.False(vm.IsSaving);
    }
    [Fact] public async Task FilteredGroupReviewAffectsOnlyVisibleMembersAndKeepsWholeGroupSummary()
    {
        var result = Grouped(); var vm = new ComparisonResultsViewModel(result, new Workflow(result));
        await vm.ReviewSelectedCommand.ExecuteAsync("Confirmed"); vm.StatusFilter = "未处理";
        Assert.Single(vm.Entries[0].Members); Assert.Equal("部分已审阅", vm.Entries[0].ReviewLabel);
        await vm.ReviewGroupCommand.ExecuteAsync("Ignored"); Assert.Empty(vm.Entries);
        Assert.Equal(ComparisonReviewState.Confirmed, vm.Changes[0].ReviewState); Assert.Equal(ComparisonReviewState.Ignored, vm.Changes[1].ReviewState);
    }
    [Fact] public async Task UndoGroupRestoresMixedPriorStatesAndCanReloadFinalState()
    {
        var result = Grouped(); var workflow = new Workflow(result); var vm = new ComparisonResultsViewModel(result, workflow);
        await vm.ReviewSelectedCommand.ExecuteAsync("Confirmed");
        await vm.ReviewGroupCommand.ExecuteAsync("Ignored"); Assert.Empty(vm.Entries); Assert.True(vm.CanUndo);
        await vm.UndoReviewCommand.ExecuteAsync(null);
        Assert.Equal(ComparisonReviewState.Confirmed, vm.Changes[0].ReviewState);
        Assert.Equal(ComparisonReviewState.Unresolved, vm.Changes[1].ReviewState);
        Assert.Contains("已审阅 1", vm.ReviewStatistics);
        await vm.UndoReviewCommand.ExecuteAsync(null); Assert.False(vm.CanUndo);
        var reopened = new ComparisonResultsViewModel((await workflow.LoadAsync(result.Record.RecordId))!, workflow);
        Assert.All(reopened.Changes, c => Assert.Equal(ComparisonReviewState.Unresolved, c.ReviewState)); Assert.False(reopened.CanUndo);
    }
    [Fact] public async Task UndoDoesNotOverwriteNewerReviewAndFailureDoesNotConsumeUndo()
    {
        var result = Grouped(); var workflow = new Workflow(result); var vm = new ComparisonResultsViewModel(result, workflow);
        await vm.ReviewSelectedCommand.ExecuteAsync("Confirmed");
        await workflow.UpdateReviewAsync(result.Record.RecordId, ["one"], ComparisonReviewState.Ignored);
        await vm.UndoReviewCommand.ExecuteAsync(null); Assert.True(vm.CanUndo); Assert.Contains("无法撤销", vm.ReviewMessage);
        Assert.Equal(ComparisonReviewState.Ignored, workflow.Record.ReviewStates["one"]);
    }
    [Fact] public void NavigationTraversesGroupMembersAndPreservesSelectionThroughFilteringAndGrouping()
    {
        var vm = new ComparisonResultsViewModel(Grouped()); Assert.False(vm.CanPrevious); Assert.True(vm.CanNext);
        vm.NextChangeCommand.Execute(null); Assert.Equal("two", vm.SelectedChange!.ChangeId); Assert.False(vm.CanNext);
        vm.Grouped = false; Assert.Equal("two", vm.SelectedEntry!.Id); Assert.Equal("two", vm.SelectedChange.ChangeId);
        vm.SearchText = "30"; Assert.Equal("two", vm.SelectedChange.ChangeId);
        vm.PreviousChangeCommand.Execute(null); Assert.Equal("one", vm.SelectedEntry!.Id);
        vm.SearchText = "missing"; Assert.False(vm.CanPrevious); Assert.False(vm.CanNext); Assert.Null(vm.SelectedChange);
    }
    [Fact] public async Task ReviewAndFilterKeepContextBoundToTheVisibleSelectedChange()
    {
        var first = ComparisonResultsTests.Change() with { BaselineNodeId = "p0", CurrentNodeId = "p0" };
        var second = ComparisonResultsTests.Change("two") with { BaselineNodeId = "p1", CurrentNodeId = "p1" };
        var result = ComparisonResultsTests.Result(first, second) with
        {
            Baseline = DocumentSnapshot.Empty with { Paragraphs = [ComparisonPreviewTests.Paragraph("p0", 0), ComparisonPreviewTests.Paragraph("p1", 1)] },
            Current = DocumentSnapshot.Empty with { Paragraphs = [ComparisonPreviewTests.Paragraph("p0", 0, "付款期限60日"), ComparisonPreviewTests.Paragraph("p1", 1, "付款期限60日")] },
        };
        var vm = new ComparisonResultsViewModel(result, new Workflow(result));
        Assert.Equal("p0", vm.Preview.BaselineContext.TargetNodeId);
        await vm.ReviewSelectedCommand.ExecuteAsync("Ignored");
        Assert.Equal("two", vm.SelectedChange?.ChangeId);
        Assert.Equal("p1", vm.Preview.BaselineContext.TargetNodeId);
        Assert.Equal("p1", vm.Preview.CurrentContext.TargetNodeId);
        vm.StatusFilter = "已忽略";
        Assert.Equal("one", vm.SelectedChange?.ChangeId);
        Assert.Equal("p0", vm.Preview.BaselineContext.TargetNodeId);
        await vm.UndoReviewCommand.ExecuteAsync(null);
        Assert.Null(vm.SelectedChange);
        Assert.Empty(vm.Preview.BaselineContext.Blocks);
        vm.StatusFilter = "全部";
        Assert.Equal("p0", vm.Preview.BaselineContext.TargetNodeId);
    }
}
