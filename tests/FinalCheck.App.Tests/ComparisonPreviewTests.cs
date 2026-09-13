using System.Diagnostics;
using FinalCheck.App.ViewModels;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using Xunit.Abstractions;

namespace FinalCheck.App.Tests;

public sealed class ComparisonPreviewTests(ITestOutputHelper output)
{
    private static readonly string[] TableOrder = ["before", "row", "after"];
    internal static DocumentParagraphSnapshot Paragraph(string id, int source, string text = "付款期限30日") =>
        new(new(id, null, DocumentNodeKind.Paragraph, id, "", source), source, text, text, false, null,
            [new(new(id + "r", id, DocumentNodeKind.Run, id + "/r", "", 0), 0, id, text, text, false, [], CharacterFormatSnapshot.Empty,
                CharacterFormatSnapshot.Empty with { Bold = true, FontSizeHalfPoints = 24, Fonts = FontFamilySnapshot.Empty with { EastAsia = "宋体" } })],
            ParagraphFormatSnapshot.Empty, ParagraphFormatSnapshot.Empty, null);
    private static ComparisonNodeMapping Mapping(string left, string right, ComparisonConfidenceLevel confidence = ComparisonConfidenceLevel.High) =>
        new(new(left, DocumentNodeKind.Paragraph, 0), new(right, DocumentNodeKind.Paragraph, 0), ComparisonMappingType.SimilarText, confidence, .9, [], false, true);
    [Fact] public void PreviewUsesExactContentFormattingAndEngineSpanWithoutChangingSnapshots()
    {
        var result = ComparisonResultsTests.Result(ComparisonResultsTests.Change());
        var p = Paragraph("p0", 0); result = result with { Baseline = result.Baseline with { Paragraphs = [p] },
            Current = result.Current with { Paragraphs = [Paragraph("p0", 0, "付款期限60日")] } };
        var vm = new ComparisonResultsViewModel(result);
        var segments = vm.Preview.Baseline[0].Cells[0].Paragraphs[0].Segments;
        Assert.Equal(p.DisplayText, string.Concat(segments.Select(s => s.Text))); Assert.Equal("30", string.Concat(segments.Where(s => s.Changed).Select(s => s.Text)));
        Assert.All(segments, s => { Assert.True(s.Format.Bold); Assert.Equal("宋体", s.Format.Fonts.EastAsia); });
        Assert.Same(p, result.Baseline.Paragraphs[0]); Assert.Equal("p0", vm.Preview.LocatedBaseline!.NodeId);
        Assert.Equal("60", string.Concat(vm.Preview.Current[0].Cells[0].Paragraphs[0].Segments.Where(s => s.Changed).Select(s => s.Text)));
    }
    [Fact] public void LogicalScrollUsesMappingsAcrossInsertedRowsAndRelinksFromLastViewport()
    {
        var outcome = ComparisonResultsTests.Result() with
        {
            Baseline = DocumentSnapshot.Empty with { Paragraphs = [Paragraph("b0", 0), Paragraph("b1", 1), Paragraph("b2", 2)] },
            Current = DocumentSnapshot.Empty with { Paragraphs = [Paragraph("c0", 0), Paragraph("new", 1), Paragraph("c1", 2), Paragraph("c2", 3)] }
        };
        outcome = outcome with { Result = outcome.Result with { NodeMappings = [Mapping("b0", "c0"), Mapping("b1", "c1"), Mapping("b2", "c2")] } };
        var vm = new ComparisonPreviewViewModel(outcome); var requests = new List<(bool, int)>(); vm.ScrollRequested += (side, row) => requests.Add((side, row));
        vm.ViewportMoved(true, 1); Assert.Equal((false, 2), requests.Single()); requests.Clear();
        vm.Linked = false; vm.ViewportMoved(false, 3); Assert.Empty(requests);
        vm.Linked = true; Assert.Equal((true, 2), requests.Single());
    }
    [Fact] public void UnmatchedAndLowConfidenceScrollNeverGuesses()
    {
        var outcome = ComparisonResultsTests.Result() with { Baseline = DocumentSnapshot.Empty with { Paragraphs = [Paragraph("b", 0)] },
            Current = DocumentSnapshot.Empty with { Paragraphs = [Paragraph("c", 0)] } };
        outcome = outcome with { Result = outcome.Result with { NodeMappings = [Mapping("b", "c", ComparisonConfidenceLevel.Low)] } };
        var vm = new ComparisonPreviewViewModel(outcome); var called = false; vm.ScrollRequested += (_, _) => called = true;
        vm.ViewportMoved(true, 0); Assert.False(called); Assert.Contains("无可靠匹配", vm.Notice);
        vm.Locate(ComparisonResultsTests.Change() with { BaselineNodeId = "unknown", CurrentNodeId = "missing" });
        Assert.Null(vm.LocatedBaseline); Assert.Null(vm.LocatedCurrent); Assert.Contains("暂无法", vm.Notice);
    }
    [Fact] public void TableRowsRemainBetweenBodyParagraphsAndCellRunCanBeLocated()
    {
        var cp = Paragraph("cp", 0); var cell = new DocumentTableCellSnapshot(new("cell", "row", DocumentNodeKind.Cell, "", "", 0), 0, 0, 0, cp.DisplayText, [cp], TableCellFormatSnapshot.Empty, 0);
        var row = new DocumentTableRowSnapshot(new("row", "table", DocumentNodeKind.Row, "", "", 0), 0, [cell], null, null);
        var table = new DocumentTableSnapshot(new("table", null, DocumentNodeKind.Table, "", "", 1), 0, [row], TableFormatSnapshot.Empty);
        var outcome = ComparisonResultsTests.Result() with { Baseline = DocumentSnapshot.Empty with { Paragraphs = [Paragraph("before", 0), Paragraph("after", 2)], Tables = [table] } };
        var vm = new ComparisonPreviewViewModel(outcome); Assert.Equal(TableOrder, vm.Baseline.Select(b => b.NodeId));
        Assert.True(vm.Baseline[1].IsTable); vm.Locate(ComparisonResultsTests.Change() with { BaselineNodeId = "cpr", CurrentNodeId = null });
        Assert.Equal("row", vm.LocatedBaseline!.NodeId); Assert.Null(vm.LocatedCurrent);
    }
    [Fact] public void MismatchedRunProjectionPreservesTextAndSkipsUnreliableFormatting()
    {
        var paragraph = Paragraph("p", 0) with { DisplayText = "与 Run 不同的真实 Snapshot 文本" };
        var outcome = ComparisonResultsTests.Result() with { Baseline = DocumentSnapshot.Empty with { Paragraphs = [paragraph] } };
        var segments = new ComparisonPreviewViewModel(outcome).Baseline[0].Cells[0].Paragraphs[0].Segments;
        Assert.Equal(paragraph.DisplayText, string.Concat(segments.Select(s => s.Text))); Assert.All(segments, s => Assert.Equal(CharacterFormatSnapshot.Empty, s.Format));
    }
    [Fact] public async Task LargePreviewProjectionIsBackgroundAndKeepsNavigationResponsive()
    {
        var paragraphs = Enumerable.Range(0, 4000).Select(i => Paragraph("p" + i, i)).ToArray();
        var outcome = ComparisonResultsTests.Result(ComparisonResultsTests.Change() with { BaselineNodeId = "p3999", CurrentNodeId = "p3999" });
        outcome = outcome with { Baseline = DocumentSnapshot.Empty with { Paragraphs = paragraphs }, Current = DocumentSnapshot.Empty with { Paragraphs = paragraphs } };
        var watch = Stopwatch.StartNew(); var pending = ComparisonResultsViewModel.CreateAsync(outcome);
        var main = new MainViewModel(); main.NavigateCommand.Execute("about"); Assert.True(main.IsOther);
        var vm = await pending; watch.Stop(); Assert.Equal(4000, vm.Preview.Baseline.Count); Assert.Equal("p3999", vm.Preview.LocatedCurrent!.NodeId);
        output.WriteLine($"4000-paragraph immutable projection: {watch.Elapsed.TotalMilliseconds:F1} ms (no UI containers created).");
    }
}
