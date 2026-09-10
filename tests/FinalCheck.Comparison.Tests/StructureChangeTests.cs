using FinalCheck.Comparison;
using FinalCheck.Core.Comparisons;

namespace FinalCheck.Comparison.Tests;

public sealed class StructureChangeTests
{
    [Fact]
    public void SingleParagraphMoveIsNotReportedAsDeleteAndInsert()
    {
        var result = Compare(
            ["第一段", "第二段", "第三段"],
            ["第二段", "第三段", "第一段"]);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ComparisonChangeKind.ParagraphMove, change.Kind);
        Assert.Equal("第一段", change.BaselineText);
        Assert.Equal("第一段", change.CurrentText);
        Assert.Equal(1, result.Statistics.ParagraphsMoved);
    }

    [Fact]
    public void MultipleParagraphMoveUsesMinimalOrderBreakingSet()
    {
        var result = Compare(
            ["A条款", "B条款", "C条款", "D条款", "E条款"],
            ["C条款", "D条款", "E条款", "A条款", "B条款"]);

        Assert.Equal(2, result.Changes.Count(change => change.Kind == ComparisonChangeKind.ParagraphMove));
        Assert.Equal(2, result.Statistics.ParagraphsMoved);
    }

    [Fact]
    public void MovedAndModifiedParagraphCarriesLocalizedTextDiff()
    {
        var result = Compare(
            ["付款期限为30日", "保密义务持续有效", "争议提交法院解决"],
            ["保密义务持续有效", "争议提交法院解决", "付款期限为60日"]);

        var change = Assert.Single(result.Changes, change =>
            change.Kind == ComparisonChangeKind.ParagraphMoveAndModify);
        var span = Assert.Single(change.DifferenceSpans);
        Assert.Equal("30", span.OldText);
        Assert.Equal("60", span.NewText);
        Assert.NotEqual(change.BaselineLocation, change.CurrentLocation);
    }

    [Fact]
    public void RepeatedParagraphsRemainConservativeAndDeterministic()
    {
        var result = Compare(
            ["定义", "相同内容", "甲方义务", "相同内容", "乙方义务"],
            ["新增说明", "定义", "相同内容", "甲方义务", "相同内容", "乙方义务"]);

        Assert.DoesNotContain(result.Changes, change =>
            change.Kind is ComparisonChangeKind.ParagraphMove or ComparisonChangeKind.ParagraphMoveAndModify);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "DuplicateCandidate");
    }

    [Fact]
    public void AddedParagraphProducesParagraphInsert()
    {
        var result = Compare(["保留"], ["保留", "新增"]);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ComparisonChangeKind.ParagraphInsert, change.Kind);
        Assert.Equal("新增", change.CurrentText);
        Assert.Null(change.BaselineNodeId);
    }

    [Fact]
    public void DeletedParagraphProducesParagraphDelete()
    {
        var result = Compare(["保留", "删除"], ["保留"]);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ComparisonChangeKind.ParagraphDelete, change.Kind);
        Assert.Equal("删除", change.BaselineText);
        Assert.Null(change.CurrentNodeId);
    }

    [Fact]
    public void FrontInsertionDoesNotCreateFalseMoves()
    {
        var result = Compare(["A条款", "B条款", "C条款"], ["前言", "A条款", "B条款", "C条款"]);

        Assert.Single(result.Changes);
        Assert.DoesNotContain(result.Changes, change =>
            change.Kind is ComparisonChangeKind.ParagraphMove or ComparisonChangeKind.ParagraphMoveAndModify);
    }

    private static ComparisonResult Compare(string[] baseline, string[] current)
    {
        var engine = ComparisonFixtureFactory.Engine();
        return engine.Compare(
            ComparisonFixtureFactory.FromText(baseline),
            ComparisonFixtureFactory.FromText(current));
    }
}
