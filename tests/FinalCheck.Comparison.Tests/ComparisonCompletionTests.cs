using System.Diagnostics;
using System.Text.Json;
using FinalCheck.Comparison;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using Xunit.Abstractions;

namespace FinalCheck.Comparison.Tests;

public sealed class ComparisonCompletionTests(ITestOutputHelper output)
{
    [Fact]
    public void NativeRevisionOnActualChangeIsLinkedAsAdditionalEvidence()
    {
        var baseline = ComparisonFixtureFactory.FromText("付款期限30日");
        var current = ComparisonFixtureFactory.FromText("付款期限60日") with
        {
            Revisions = [Revision("revision-1", "body/p[0]", "60")],
        };

        var change = Assert.Single(ComparisonFixtureFactory.Engine().Compare(baseline, current).Changes);

        Assert.Equal(["revision-1"], change.RevisionIds);
        Assert.Contains(ComparisonEvidenceKind.SnapshotDifference, change.SourceEvidence);
        Assert.Contains(ComparisonEvidenceKind.NativeRevision, change.SourceEvidence);
    }

    [Fact]
    public void AcceptedRevisionStillProducesActualSnapshotDifference()
    {
        var result = ComparisonFixtureFactory.Engine().Compare(
            ComparisonFixtureFactory.FromText("付款期限30日，双方应依约履行"),
            ComparisonFixtureFactory.FromText("付款期限60日，双方应依约履行"));

        var change = Assert.Single(result.Changes);
        Assert.Empty(change.RevisionIds);
        Assert.Contains(ComparisonEvidenceKind.SnapshotDifference, change.SourceEvidence);
    }

    [Fact]
    public void CommentOnChangeIsLinkedWithoutDuplicateChange()
    {
        var baseline = ComparisonFixtureFactory.FromText("付款期限30日，双方应依约履行");
        var current = ComparisonFixtureFactory.FromText("付款期限60日，双方应依约履行") with
        {
            Comments = [Comment("comment-1", "body/p[0]", "请确认期限")],
        };

        var result = ComparisonFixtureFactory.Engine().Compare(baseline, current);
        var change = Assert.Single(result.Changes);

        Assert.Equal(["comment-1"], change.CommentIds);
        Assert.Contains(ComparisonEvidenceKind.Comment, change.SourceEvidence);
        Assert.Equal(1, result.Statistics.Comments);
    }

    [Fact]
    public void CommentWithoutChangeRemainsIndependentlyAccessible()
    {
        var baseline = ComparisonFixtureFactory.FromText("不变内容");
        var current = ComparisonFixtureFactory.FromText("不变内容") with
        {
            Comments = [Comment("comment-2", "body/p[0]", "仅批注")],
        };

        var change = Assert.Single(ComparisonFixtureFactory.Engine().Compare(baseline, current).Changes);

        Assert.Equal(ComparisonChangeKind.Comment, change.Kind);
        Assert.Equal("仅批注", change.CurrentText);
    }

    [Fact]
    public void UnmappedRevisionProducesDiagnosticAndIndependentEvidence()
    {
        var baseline = ComparisonFixtureFactory.FromText("不变内容");
        var current = ComparisonFixtureFactory.FromText("不变内容") with
        {
            Revisions = [Revision("revision-missing", "missing-node", "新增")],
        };

        var result = ComparisonFixtureFactory.Engine().Compare(baseline, current);

        Assert.Equal(ComparisonChangeKind.NativeRevision, Assert.Single(result.Changes).Kind);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RevisionMappingFailed");
    }

    [Fact]
    public void RepeatedExactReplacementCreatesOneViewGroup()
    {
        var result = ComparisonFixtureFactory.Engine().Compare(
            ComparisonFixtureFactory.FromText(
                "本合同约定甲方应当按期付款并履行全部义务",
                "保密条款约定甲方应当妥善保护全部信息",
                "交付条款约定甲方应当按时提交全部材料"),
            ComparisonFixtureFactory.FromText(
                "本合同约定乙方应当按期付款并履行全部义务",
                "保密条款约定乙方应当妥善保护全部信息",
                "交付条款约定乙方应当按时提交全部材料"));

        var group = Assert.Single(result.Groups);
        Assert.Equal(3, group.ChangeIds.Count);
        Assert.Equal(3, result.Changes.Count);
        Assert.Equal(1, result.Statistics.GroupCount);
    }

    [Fact]
    public void SerializerRoundTripsCoreResultAndRejectsUnknownSchema()
    {
        var serializer = new JsonComparisonResultSerializer();
        var result = ComparisonFixtureFactory.Engine().Compare(
            ComparisonFixtureFactory.FromText("期限30日"),
            ComparisonFixtureFactory.FromText("期限60日"));

        var payload = serializer.Serialize(result);
        var loaded = serializer.Deserialize(payload);

        Assert.Equal(payload, serializer.Serialize(loaded));
        var unsupported = result with { ComparisonSchemaVersion = 99 };
        Assert.Throws<NotSupportedException>(() => serializer.Serialize(unsupported));
    }

    [Fact]
    public void SameSnapshotsProduceByteForByteDeterministicResults()
    {
        var baseline = ComparisonFixtureFactory.FromText("A条款", "甲方付款", "结尾");
        var current = ComparisonFixtureFactory.FromText("甲方付款", "A条款修改", "结尾");
        var serializer = new JsonComparisonResultSerializer();
        var engine = ComparisonFixtureFactory.Engine();

        var first = serializer.Serialize(engine.Compare(baseline, current));
        var second = serializer.Serialize(engine.Compare(baseline, current));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ProgressReportsEveryPipelineStageInOrder()
    {
        var progress = new CapturingProgress();

        ComparisonFixtureFactory.Engine().Compare(
            ComparisonFixtureFactory.FromText("期限30日"),
            ComparisonFixtureFactory.FromText("期限60日"),
            progress);

        Assert.Equal(
            [
                ComparisonStage.Preparing,
                ComparisonStage.MatchingStructure,
                ComparisonStage.DetectingMoves,
                ComparisonStage.ComparingText,
                ComparisonStage.ComparingFormatting,
                ComparisonStage.ProcessingRevisionsAndComments,
                ComparisonStage.GroupingChanges,
                ComparisonStage.Completed,
            ],
            progress.Values.Select(value => value.Stage));
    }

    [Theory]
    [InlineData(10, "small")]
    [InlineData(500, "medium")]
    public void SyntheticPerformanceFixtureRecordsSizeTimeAllocationsAndChanges(int paragraphCount, string fixtureName)
    {
        var baselineText = Enumerable.Range(0, paragraphCount)
            .Select(index => $"第{index}条 付款期限为30日，双方应当履行约定。")
            .ToArray();
        var currentText = baselineText
            .Select((text, index) => index % 50 == 0 ? text.Replace("30日", "60日", StringComparison.Ordinal) : text)
            .ToArray();
        var baseline = ComparisonFixtureFactory.FromText(baselineText);
        var current = ComparisonFixtureFactory.FromText(currentText);
        var baselineSize = JsonSerializer.SerializeToUtf8Bytes(baseline).Length;
        var currentSize = JsonSerializer.SerializeToUtf8Bytes(current).Length;
        var beforeAllocations = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        var result = ComparisonFixtureFactory.Engine().Compare(baseline, current);

        stopwatch.Stop();
        var allocations = GC.GetAllocatedBytesForCurrentThread() - beforeAllocations;
        output.WriteLine(
            $"fixture={fixtureName}; baselineBytes={baselineSize}; currentBytes={currentSize}; paragraphs={paragraphCount}; elapsedMs={stopwatch.ElapsedMilliseconds}; allocatedBytes={allocations}; changes={result.Changes.Count}");
        Assert.Equal((paragraphCount + 49) / 50, result.Changes.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    private static DocumentRevisionSnapshot Revision(string revisionId, string nodeId, string text) => new(
        new DocumentNodeIdentitySnapshot(
            $"revision/{revisionId}",
            nodeId,
            DocumentNodeKind.Run,
            $"revision/{revisionId}",
            "/word/document.xml",
            0),
        revisionId,
        DocumentRevisionKind.Insert,
        text,
        "Reviewer",
        new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
        nodeId,
        nodeId,
        null,
        true,
        null,
        null,
        null);

    private static DocumentCommentSnapshot Comment(string commentId, string nodeId, string text) => new(
        commentId,
        text,
        "Reviewer",
        "R",
        new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
        nodeId,
        nodeId,
        nodeId,
        null,
        true);

    private sealed class CapturingProgress : IProgress<ComparisonProgress>
    {
        public List<ComparisonProgress> Values { get; } = [];

        public void Report(ComparisonProgress value) => Values.Add(value);
    }
}
