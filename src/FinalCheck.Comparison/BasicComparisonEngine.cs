using System.Security.Cryptography;
using System.Text;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public sealed class BasicComparisonEngine(
    IStructureMatcher structureMatcher,
    ITextDiffService textDiffService,
    IParagraphMoveDetector moveDetector) : IComparisonEngine
{
    public const string AlgorithmVersion = "comparison-v0.1";

    public ComparisonResult Compare(
        DocumentSnapshot baseline,
        DocumentSnapshot current,
        IProgress<ComparisonProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ComparisonProgress(ComparisonStage.Preparing, 0, null));
        progress?.Report(new ComparisonProgress(
            ComparisonStage.MatchingStructure,
            0,
            baseline.Paragraphs.Count + current.Paragraphs.Count));
        var matchResult = structureMatcher.Match(baseline, current, cancellationToken);
        progress?.Report(new ComparisonProgress(
            ComparisonStage.DetectingMoves,
            0,
            matchResult.Mappings.Count));
        var mappings = moveDetector.Detect(matchResult.Mappings, cancellationToken);
        var lowConfidenceCount = mappings.Count(mapping =>
            mapping.Confidence == ComparisonConfidenceLevel.Low);
        progress?.Report(new ComparisonProgress(
            ComparisonStage.ComparingText,
            0,
            mappings.Count));
        var baselineById = baseline.Paragraphs.ToDictionary(paragraph => paragraph.NodeId, StringComparer.Ordinal);
        var currentById = current.Paragraphs.ToDictionary(paragraph => paragraph.NodeId, StringComparer.Ordinal);
        var changes = new List<ComparisonChangeItem>();
        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!baselineById.TryGetValue(mapping.BaselineNode.NodeId, out var baselineParagraph) ||
                !currentById.TryGetValue(mapping.CurrentNode.NodeId, out var currentParagraph))
            {
                continue;
            }

            var spans = mapping.IsModified
                ? textDiffService.Compare(
                    baselineParagraph.DisplayText,
                    currentParagraph.DisplayText,
                    cancellationToken)
                : [];
            if (!mapping.IsMoved && spans.Count == 0)
            {
                continue;
            }

            changes.Add(new ComparisonChangeItem(
                mapping.IsMoved
                    ? $"move:{mapping.BaselineNode.NodeId}:{mapping.CurrentNode.NodeId}"
                    : $"text:{mapping.BaselineNode.NodeId}:{mapping.CurrentNode.NodeId}",
                mapping.IsMoved
                    ? spans.Count == 0
                        ? ComparisonChangeKind.ParagraphMove
                        : ComparisonChangeKind.ParagraphMoveAndModify
                    : ChangeKind(spans),
                mapping.BaselineNode.NodeId,
                mapping.CurrentNode.NodeId,
                mapping.BaselineNode.StructuralPath,
                mapping.CurrentNode.StructuralPath,
                baselineParagraph.DisplayText,
                currentParagraph.DisplayText,
                spans,
                null,
                [],
                [],
                [ComparisonEvidenceKind.SnapshotDifference],
                mapping.Confidence,
                []));
        }

        changes.AddRange(matchResult.UnmatchedBaseline.Select(paragraph => ParagraphChange(
            paragraph,
            ComparisonChangeKind.ParagraphDelete)));
        changes.AddRange(matchResult.UnmatchedCurrent.Select(paragraph => ParagraphChange(
            paragraph,
            ComparisonChangeKind.ParagraphInsert)));
        changes.Sort(static (left, right) => string.CompareOrdinal(left.ChangeId, right.ChangeId));

        var paragraphsAdded = changes.Count(change => change.Kind == ComparisonChangeKind.ParagraphInsert);
        var paragraphsDeleted = changes.Count(change => change.Kind == ComparisonChangeKind.ParagraphDelete);
        var paragraphsMoved = changes.Count(change =>
            change.Kind is ComparisonChangeKind.ParagraphMove or ComparisonChangeKind.ParagraphMoveAndModify);
        var textChanges = changes.Count(change => change.DifferenceSpans.Count > 0);

        var result = new ComparisonResult(
            ComparisonResult.CurrentSchemaVersion,
            new ComparisonMetadata(
                SnapshotIdentity(baseline),
                SnapshotIdentity(current),
                baseline.SnapshotSchemaVersion,
                current.SnapshotSchemaVersion,
                AlgorithmVersion),
            mappings,
            changes,
            [],
            matchResult.Diagnostics,
            ComparisonStatistics.Empty with
            {
                TotalChanges = changes.Count,
                TextChanges = textChanges,
                ParagraphsAdded = paragraphsAdded,
                ParagraphsDeleted = paragraphsDeleted,
                ParagraphsMoved = paragraphsMoved,
                LowConfidenceMappings = lowConfidenceCount,
            });
        progress?.Report(new ComparisonProgress(
            ComparisonStage.Completed,
            baseline.Paragraphs.Count + current.Paragraphs.Count,
            baseline.Paragraphs.Count + current.Paragraphs.Count));
        return result;
    }

    private static ComparisonChangeItem ParagraphChange(
        DocumentParagraphSnapshot paragraph,
        ComparisonChangeKind kind)
    {
        var isInsert = kind == ComparisonChangeKind.ParagraphInsert;
        return new ComparisonChangeItem(
            $"{(isInsert ? "insert" : "delete")}:{paragraph.NodeId}",
            kind,
            isInsert ? null : paragraph.NodeId,
            isInsert ? paragraph.NodeId : null,
            isInsert ? null : paragraph.Identity.StructuralPath,
            isInsert ? paragraph.Identity.StructuralPath : null,
            isInsert ? string.Empty : paragraph.DisplayText,
            isInsert ? paragraph.DisplayText : string.Empty,
            [],
            null,
            [],
            [],
            [ComparisonEvidenceKind.SnapshotDifference],
            null,
            []);
    }

    private static ComparisonChangeKind ChangeKind(IReadOnlyList<DifferenceSpan> spans)
    {
        if (spans.All(span => span.Operation == DifferenceOperation.Insert))
        {
            return ComparisonChangeKind.TextInsert;
        }

        return spans.All(span => span.Operation == DifferenceOperation.Delete)
            ? ComparisonChangeKind.TextDelete
            : ComparisonChangeKind.TextReplace;
    }

    private static string SnapshotIdentity(DocumentSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Metadata.Sha256))
        {
            return "sha256:" + snapshot.Metadata.Sha256;
        }

        var builder = new StringBuilder();
        builder.Append(snapshot.SnapshotSchemaVersion).Append('|');
        foreach (var paragraph in snapshot.Paragraphs)
        {
            builder.Append(paragraph.NodeId).Append(':').Append(paragraph.DisplayText).Append('|');
        }

        foreach (var table in snapshot.Tables)
        {
            builder.Append(table.NodeId).Append(':');
            foreach (var cell in table.Rows.SelectMany(row => row.Cells))
            {
                builder.Append(cell.NodeId).Append('=').Append(cell.DisplayText).Append(';');
            }
        }

        return "derived-sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}
