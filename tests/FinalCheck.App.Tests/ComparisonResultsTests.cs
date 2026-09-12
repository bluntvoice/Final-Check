using FinalCheck.App.ViewModels;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.App.Tests;

public sealed class ComparisonResultsTests
{
    internal static ComparisonChangeItem Change(string id = "one", ComparisonChangeKind kind = ComparisonChangeKind.TextReplace) =>
        new(id, kind, "p0", "p0", "body/p[0]", "body/p[1]", "付款期限30日", "付款期限60日",
            [new(DifferenceOperation.Replace, 4, 2, 4, 2, "30", "60")], null, [], [], [], ComparisonConfidenceLevel.High, []);
    internal static ComparisonWorkflowResult Result(params ComparisonChangeItem[] changes)
    {
        var empty = ComparisonExecutionTests.Result();
        return empty with { Record = empty.Record with { ReviewStates = changes.ToDictionary(c => c.ChangeId, _ => ComparisonReviewState.Unresolved) },
            Result = empty.Result with { Changes = changes, Statistics = ComparisonStatistics.Empty with { TotalChanges = changes.Length } } };
    }
    [Fact] public void NoChangesHasHonestEmptyOverview()
    {
        var vm = new ComparisonResultsViewModel(Result()); Assert.False(vm.HasChanges); Assert.Empty(vm.Entries);
        Assert.Contains("没有检测到修改", vm.EmptyMessage); Assert.Contains("共 0 项", vm.Statistics);
    }
    [Fact] public void GroupedAndIndividualShareTheActualEngineItems()
    {
        var first = Change(); var second = Change("two"); var result = Result(first, second);
        result = result with { Result = result.Result with { Groups = [new("group", first.Kind, "30", "60", ["one", "two"])] } };
        var vm = new ComparisonResultsViewModel(result); Assert.True(vm.Grouped); Assert.Single(vm.Entries); Assert.Equal(2, vm.Entries[0].Members.Count);
        Assert.Same(first, vm.Changes[0].Item); vm.Entries[0].Members[1].SelectCommand.Execute(null); Assert.Same(vm.Changes[1], vm.SelectedChange);
        vm.Grouped = false; Assert.Equal(2, vm.Entries.Count); Assert.Same(vm.Changes[1], vm.Entries[1].Members[0]);
    }
    [Fact] public void DifferenceSpansHighlightOnlyChangedUtf16Range()
    {
        var vm = new ComparisonResultsViewModel(Result(Change()));
        Assert.Equal("30", string.Concat(vm.SelectedChange!.BaselineSegments.Where(s => s.Changed).Select(s => s.Text)));
        Assert.Equal("60", string.Concat(vm.SelectedChange.CurrentSegments.Where(s => s.Changed).Select(s => s.Text)));
        Assert.Equal("付款期限30日", string.Concat(vm.SelectedChange.BaselineSegments.Select(s => s.Text)));
        var spans = new[] { new DifferenceSpan(DifferenceOperation.Replace, 2, 1, 2, 1, "a", "b") };
        Assert.Equal("😀a中", string.Concat(DifferenceHighlight.Build("😀a中", spans, true).Select(s => s.Text)));
    }
    [Fact] public void FormatsCombinePropertiesAndMovesExposeBothLocations()
    {
        var change = Change(kind: ComparisonChangeKind.CharacterFormatChange) with { DifferenceSpans = [], FormatDifference = new(FormatDifferenceScope.Character, "p0", "p0",
            [new("Font.EastAsia", "宋体", "仿宋"), new("FontSizeHalfPoints", "24", "20"), new("Bold", "False", "True")]) };
        var vm = new ComparisonResultsViewModel(Result(change, Change("move", ComparisonChangeKind.ParagraphMoveAndModify)));
        Assert.Equal(3, vm.SelectedChange!.FormatDetails.Count); Assert.Contains("中文字体：宋体 → 仿宋", vm.SelectedChange.FormatDetails);
        Assert.Contains("字号：12 磅 → 10 磅", vm.SelectedChange.FormatDetails);
        vm.SelectedEntry = vm.Entries[1]; Assert.Equal("移动并修改", vm.SelectedChange!.TypeLabel); Assert.Contains("原位置", vm.SelectedChange.Location); Assert.Contains("当前位置", vm.SelectedChange.Location);
    }
    [Fact] public void MediumConfidenceAndPartialAreNeverShownAsCertain()
    {
        var result = Result(Change() with { MatchConfidence = ComparisonConfidenceLevel.Medium, DiagnosticCodes = ["AmbiguousParagraphMatch"] });
        result = result with { Current = result.Current with { ParseStatus = DocumentParseStatus.Partial } };
        var vm = new ComparisonResultsViewModel(result); Assert.True(vm.SelectedChange!.LowConfidence); Assert.Contains("人工确认", vm.SelectedChange.ConfidenceText);
        Assert.Contains("可能不完整", vm.IntegrityNotice); Assert.Contains("AmbiguousParagraphMatch", vm.SelectedChange.Diagnostics);
    }
    [Fact] public void CommentsAndNativeRevisionsAreKeptAsAdditionalEvidence()
    {
        var result = Result(Change() with { CommentIds = ["comment"], RevisionIds = ["revision"] });
        result = result with { Current = result.Current with { Comments = [new("comment", "请核实付款期限", "测试作者", null, null, null, null, "p0", null, false)],
            Revisions = [new(new("revision-node", null, DocumentNodeKind.Run, "p0/r0", "", 0), "revision", DocumentRevisionKind.Insert, "60", "修订作者", null, "p0", "p0", null, true, null, null, null)] } };
        var vm = new ComparisonResultsViewModel(result); Assert.Contains("请核实付款期限", vm.SelectedChange!.CommentDetails); Assert.Contains("插入修订", vm.SelectedChange.RevisionDetails);
    }
}
