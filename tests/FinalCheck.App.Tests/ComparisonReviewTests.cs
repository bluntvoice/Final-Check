using FinalCheck.App.ViewModels;
using FinalCheck.Core.Comparisons;

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
        public Task<ComparisonWorkflowResult> ExecuteAsync(ComparisonInputValidation input, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
}
