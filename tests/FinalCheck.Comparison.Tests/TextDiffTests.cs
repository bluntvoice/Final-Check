using FinalCheck.Comparison;
using FinalCheck.Core.Comparisons;

namespace FinalCheck.Comparison.Tests;

public sealed class TextDiffTests
{
    private readonly TokenTextDiffService _service = new(new MixedLanguageTextTokenizer());

    [Theory]
    [InlineData("甲方", "乙方", "甲", "乙")]
    [InlineData("付款期限为30日", "付款期限为60日", "30", "60")]
    [InlineData("应及时履行合同", "应立即履行合同", "及时", "立即")]
    [InlineData("Payment is due today.", "Payment is due tomorrow.", "today", "tomorrow")]
    public void ReplacementHighlightsOnlyChangedTokenRange(
        string baseline,
        string current,
        string oldText,
        string newText)
    {
        var span = Assert.Single(_service.Compare(baseline, current));

        Assert.Equal(DifferenceOperation.Replace, span.Operation);
        Assert.Equal(oldText, span.OldText);
        Assert.Equal(newText, span.NewText);
        Assert.Equal(oldText, baseline.Substring(span.BaselineStart, span.BaselineLength));
        Assert.Equal(newText, current.Substring(span.CurrentStart, span.CurrentLength));
    }

    [Fact]
    public void InsertionUsesZeroLengthBaselineRange()
    {
        var span = Assert.Single(_service.Compare("合同生效", "合同立即生效"));

        Assert.Equal(DifferenceOperation.Insert, span.Operation);
        Assert.Equal(2, span.BaselineStart);
        Assert.Equal(0, span.BaselineLength);
        Assert.Equal("立即", span.NewText);
    }

    [Fact]
    public void DeletionUsesZeroLengthCurrentRange()
    {
        var span = Assert.Single(_service.Compare("合同立即生效", "合同生效"));

        Assert.Equal(DifferenceOperation.Delete, span.Operation);
        Assert.Equal("立即", span.OldText);
        Assert.Equal(2, span.CurrentStart);
        Assert.Equal(0, span.CurrentLength);
    }

    [Fact]
    public void PunctuationChangeIsLocalized()
    {
        var span = Assert.Single(_service.Compare("同意。", "同意！"));

        Assert.Equal("。", span.OldText);
        Assert.Equal("！", span.NewText);
    }

    [Fact]
    public void MultipleSeparatedChangesProduceSeparateSpans()
    {
        var spans = _service.Compare("甲方应在30日付款。", "乙方应在60日付款！");

        Assert.Equal(3, spans.Count);
        Assert.Equal(["甲", "30", "。"], spans.Select(span => span.OldText));
        Assert.Equal(["乙", "60", "！"], spans.Select(span => span.NewText));
    }

    [Fact]
    public void IdenticalTextHasNoSpansRegardlessOfRunBoundaries()
    {
        var baseline = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec(
            "Payment期限30日",
            Runs: ["Payment", "期限", "30日"]));
        var current = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec(
            "Payment期限30日",
            Runs: ["Payment期限", "30", "日"]));
        var engine = new BasicComparisonEngine(new MultiSignalParagraphMatcher(), _service);

        var result = engine.Compare(baseline, current);

        Assert.Empty(result.Changes);
    }

    [Fact]
    public void EngineCreatesStructuredTextChangeForModifiedMapping()
    {
        var baseline = ComparisonFixtureFactory.FromText("付款期限为30日");
        var current = ComparisonFixtureFactory.FromText("付款期限为60日");
        var engine = new BasicComparisonEngine(new MultiSignalParagraphMatcher(), _service);

        var result = engine.Compare(baseline, current);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ComparisonChangeKind.TextReplace, change.Kind);
        Assert.Equal("30", Assert.Single(change.DifferenceSpans).OldText);
        Assert.Equal(1, result.Statistics.TextChanges);
    }

    [Fact]
    public void TextDiffHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _service.Compare("甲方", "乙方", cancellation.Token));
    }
}
