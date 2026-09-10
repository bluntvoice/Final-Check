using System.Security.Cryptography;
using System.Text;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public sealed class BasicComparisonEngine(
    IStructureMatcher structureMatcher,
    ITextDiffService textDiffService) : IComparisonEngine
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
        var lowConfidenceCount = matchResult.Mappings.Count(mapping =>
            mapping.Confidence == ComparisonConfidenceLevel.Low);
        progress?.Report(new ComparisonProgress(
            ComparisonStage.ComparingText,
            0,
            matchResult.Mappings.Count));
        var baselineById = baseline.Paragraphs.ToDictionary(paragraph => paragraph.NodeId, StringComparer.Ordinal);
        var currentById = current.Paragraphs.ToDictionary(paragraph => paragraph.NodeId, StringComparer.Ordinal);
        var changes = new List<ComparisonChangeItem>();
        foreach (var mapping in matchResult.Mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!mapping.IsModified ||
                !baselineById.TryGetValue(mapping.BaselineNode.NodeId, out var baselineParagraph) ||
                !currentById.TryGetValue(mapping.CurrentNode.NodeId, out var currentParagraph))
            {
                continue;
            }

            var spans = textDiffService.Compare(
                baselineParagraph.DisplayText,
                currentParagraph.DisplayText,
                cancellationToken);
            if (spans.Count == 0)
            {
                continue;
            }

            changes.Add(new ComparisonChangeItem(
                $"text:{mapping.BaselineNode.NodeId}:{mapping.CurrentNode.NodeId}",
                ChangeKind(spans),
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

        var result = new ComparisonResult(
            ComparisonResult.CurrentSchemaVersion,
            new ComparisonMetadata(
                SnapshotIdentity(baseline),
                SnapshotIdentity(current),
                baseline.SnapshotSchemaVersion,
                current.SnapshotSchemaVersion,
                AlgorithmVersion),
            matchResult.Mappings,
            changes,
            [],
            matchResult.Diagnostics,
            ComparisonStatistics.Empty with
            {
                TotalChanges = changes.Count,
                TextChanges = changes.Count,
                LowConfidenceMappings = lowConfidenceCount,
            });
        progress?.Report(new ComparisonProgress(
            ComparisonStage.Completed,
            baseline.Paragraphs.Count + current.Paragraphs.Count,
            baseline.Paragraphs.Count + current.Paragraphs.Count));
        return result;
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
