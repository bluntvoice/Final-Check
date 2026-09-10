using FinalCheck.Comparison;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison.Tests;

public sealed class ParagraphMatcherTests
{
    private readonly MultiSignalParagraphMatcher _matcher = new();

    [Fact]
    public void IdenticalDocumentsMapEveryParagraphExactly()
    {
        var baseline = ComparisonFixtureFactory.FromText("第一条 总则", "甲方应付款。", "合同生效。");
        var current = ComparisonFixtureFactory.FromText("第一条 总则", "甲方应付款。", "合同生效。");

        var result = new BasicComparisonEngine(_matcher, new TokenTextDiffService(new MixedLanguageTextTokenizer()))
            .Compare(baseline, current);

        Assert.Equal(3, result.NodeMappings.Count);
        Assert.All(result.NodeMappings, mapping => Assert.Equal(ComparisonConfidenceLevel.Exact, mapping.Confidence));
        Assert.Empty(result.Changes);
        Assert.StartsWith("derived-sha256:", result.Metadata.BaselineSnapshotId, StringComparison.Ordinal);
    }

    [Fact]
    public void ModifiedWordStillProducesHighOrMediumMapping()
    {
        var baseline = ComparisonFixtureFactory.FromText("付款期限为30日");
        var current = ComparisonFixtureFactory.FromText("付款期限为60日");

        var result = _matcher.Match(baseline, current);

        var mapping = Assert.Single(result.Mappings);
        Assert.True(mapping.IsModified);
        Assert.True(mapping.Confidence is ComparisonConfidenceLevel.High or ComparisonConfidenceLevel.Medium);
        Assert.Contains(mapping.Evidence, evidence => evidence.Kind == ComparisonEvidenceKind.TextSimilarity);
    }

    [Fact]
    public void FrontInsertionDoesNotShiftFollowingMappings()
    {
        var baseline = ComparisonFixtureFactory.FromText("标题", "第一段正文", "第二段正文");
        var current = ComparisonFixtureFactory.FromText("新增说明", "标题", "第一段正文", "第二段正文");

        var result = _matcher.Match(baseline, current);

        Assert.Equal(3, result.Mappings.Count);
        Assert.Empty(result.UnmatchedBaseline);
        Assert.Equal("新增说明", Assert.Single(result.UnmatchedCurrent).DisplayText);
        Assert.Equal([1, 2, 3], result.Mappings.Select(mapping => mapping.CurrentNode.Position));
    }

    [Fact]
    public void DeletedParagraphRemainsUnmatchedWithoutBreakingOtherMappings()
    {
        var baseline = ComparisonFixtureFactory.FromText("标题", "将被删除", "保留正文");
        var current = ComparisonFixtureFactory.FromText("标题", "保留正文");

        var result = _matcher.Match(baseline, current);

        Assert.Equal(2, result.Mappings.Count);
        Assert.Equal("将被删除", Assert.Single(result.UnmatchedBaseline).DisplayText);
        Assert.Empty(result.UnmatchedCurrent);
    }

    [Fact]
    public void DuplicateShortParagraphsUseStableOrderAndReportEvidence()
    {
        var baseline = ComparisonFixtureFactory.FromText("定义", "相同短语", "甲方义务", "相同短语", "乙方义务");
        var current = ComparisonFixtureFactory.FromText("新增", "定义", "相同短语", "甲方义务", "相同短语", "乙方义务");

        var first = _matcher.Match(baseline, current);
        var second = _matcher.Match(baseline, current);

        Assert.Equal(5, first.Mappings.Count);
        Assert.Equal(
            first.Mappings.Select(mapping => (mapping.BaselineNode.Position, mapping.CurrentNode.Position)),
            second.Mappings.Select(mapping => (mapping.BaselineNode.Position, mapping.CurrentNode.Position)));
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == "DuplicateCandidate");
    }

    [Fact]
    public void HeadingAndNumberingSignalsSupportModifiedClauseMapping()
    {
        var numbering = new ParagraphNumberingReferenceSnapshot(7, 0, 3, "decimal", "%1.", 1);
        var baseline = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec(
            "第一条 付款期限为30日",
            "Heading1",
            numbering));
        var current = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec(
            "第一条 付款期限为60日",
            "Heading1",
            numbering));

        var mapping = Assert.Single(_matcher.Match(baseline, current).Mappings);

        Assert.Equal(ComparisonMappingType.HeadingOrNumbering, mapping.MappingType);
        Assert.Contains(mapping.Evidence, evidence =>
            evidence.Kind is ComparisonEvidenceKind.Heading or ComparisonEvidenceKind.Numbering);
    }

    [Fact]
    public void MixedChineseEnglishParagraphsCanBeMatched()
    {
        var baseline = ComparisonFixtureFactory.FromText("Payment 期限为 30 days，逾期需通知。");
        var current = ComparisonFixtureFactory.FromText("Payment 期限为 60 days，逾期需通知。");

        var mapping = Assert.Single(_matcher.Match(baseline, current).Mappings);

        Assert.True(mapping.Score >= 0.62);
        Assert.True(mapping.IsModified);
    }

    [Fact]
    public void RunBoundaryChangesDoNotAffectParagraphMatching()
    {
        var baseline = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec(
            "付款期限30日",
            Runs: ["付款", "期限", "30日"]));
        var current = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec(
            "付款期限30日",
            Runs: ["付款期限", "30日"]));

        var mapping = Assert.Single(_matcher.Match(baseline, current).Mappings);

        Assert.Equal(ComparisonConfidenceLevel.Exact, mapping.Confidence);
        Assert.False(mapping.IsModified);
    }

    [Fact]
    public void MatchingHonorsCancellation()
    {
        var snapshot = ComparisonFixtureFactory.FromText("第一段");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _matcher.Match(snapshot, snapshot, cancellation.Token));
    }
}
