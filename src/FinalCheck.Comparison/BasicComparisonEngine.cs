using System.Security.Cryptography;
using System.Text;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public sealed class BasicComparisonEngine(IStructureMatcher structureMatcher) : IComparisonEngine
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
        var result = new ComparisonResult(
            ComparisonResult.CurrentSchemaVersion,
            new ComparisonMetadata(
                SnapshotIdentity(baseline),
                SnapshotIdentity(current),
                baseline.SnapshotSchemaVersion,
                current.SnapshotSchemaVersion,
                AlgorithmVersion),
            matchResult.Mappings,
            [],
            [],
            matchResult.Diagnostics,
            ComparisonStatistics.Empty with { LowConfidenceMappings = lowConfidenceCount });
        progress?.Report(new ComparisonProgress(
            ComparisonStage.Completed,
            baseline.Paragraphs.Count + current.Paragraphs.Count,
            baseline.Paragraphs.Count + current.Paragraphs.Count));
        return result;
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
