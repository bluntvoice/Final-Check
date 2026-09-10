using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public sealed class SnapshotAnnotationIntegrationService : IAnnotationIntegrationService
{
    public AnnotationIntegrationResult Integrate(
        DocumentSnapshot baseline,
        DocumentSnapshot current,
        IReadOnlyList<ComparisonChangeItem> changes,
        IReadOnlyList<ComparisonNodeMapping> mappings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(mappings);
        var integrated = changes.OrderBy(change => change.ChangeId, StringComparer.Ordinal).ToList();
        var diagnostics = new List<ComparisonDiagnostic>();
        var currentLocations = mappings.ToDictionary(
            mapping => mapping.CurrentNode.NodeId,
            mapping => mapping.CurrentNode.StructuralPath,
            StringComparer.Ordinal);

        foreach (var revision in current.Revisions.OrderBy(item => item.RevisionId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targets = RevisionTargets(revision);
            var changeIndex = FindChange(integrated, targets);
            if (changeIndex >= 0)
            {
                var change = integrated[changeIndex];
                integrated[changeIndex] = change with
                {
                    RevisionIds = Append(change.RevisionIds, revision.RevisionId),
                    SourceEvidence = Append(change.SourceEvidence, ComparisonEvidenceKind.NativeRevision),
                };
            }
            else
            {
                var nodeId = targets.FirstOrDefault();
                integrated.Add(new ComparisonChangeItem(
                    $"revision:{revision.RevisionId}",
                    ComparisonChangeKind.NativeRevision,
                    null,
                    nodeId,
                    null,
                    Location(nodeId, currentLocations),
                    revision.Kind == DocumentRevisionKind.Delete ? revision.Text : string.Empty,
                    revision.Kind == DocumentRevisionKind.Insert ? revision.Text : string.Empty,
                    [],
                    RevisionFormat(revision, nodeId),
                    [revision.RevisionId],
                    [],
                    [ComparisonEvidenceKind.NativeRevision],
                    null,
                    ["RevisionMappingFailed"]));
                diagnostics.Add(new ComparisonDiagnostic(
                    ComparisonDiagnosticSeverity.Warning,
                    "RevisionMappingFailed",
                    "Native revision could not be associated with an actual Snapshot difference and remains independently accessible.",
                    null,
                    nodeId));
            }

            if (!revision.IsSupported)
            {
                diagnostics.Add(new ComparisonDiagnostic(
                    ComparisonDiagnosticSeverity.Warning,
                    "UnsupportedComparisonElement",
                    $"Native revision kind {revision.Kind} is only partially supported.",
                    null,
                    targets.FirstOrDefault()));
            }
        }

        foreach (var comment in current.Comments.OrderBy(item => item.CommentId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targets = CommentTargets(comment);
            var changeIndex = FindChange(integrated, targets);
            if (changeIndex >= 0)
            {
                var change = integrated[changeIndex];
                integrated[changeIndex] = change with
                {
                    CommentIds = Append(change.CommentIds, comment.CommentId),
                    SourceEvidence = Append(change.SourceEvidence, ComparisonEvidenceKind.Comment),
                };
                continue;
            }

            var nodeId = targets.FirstOrDefault();
            integrated.Add(new ComparisonChangeItem(
                $"comment:{comment.CommentId}",
                ComparisonChangeKind.Comment,
                null,
                nodeId,
                null,
                Location(nodeId, currentLocations),
                string.Empty,
                comment.Text,
                [],
                null,
                [],
                [comment.CommentId],
                [ComparisonEvidenceKind.Comment],
                null,
                comment.IsAnchored ? [] : ["UnanchoredComment"]));
        }

        integrated.Sort(static (left, right) => string.CompareOrdinal(left.ChangeId, right.ChangeId));
        return new AnnotationIntegrationResult(integrated, diagnostics, current.Comments.Count);
    }

    private static int FindChange(List<ComparisonChangeItem> changes, HashSet<string> targets) =>
        changes.FindIndex(change =>
            change.CurrentNodeId is not null && targets.Contains(change.CurrentNodeId));

    private static HashSet<string> RevisionTargets(DocumentRevisionSnapshot revision) =>
        Values(revision.AffectedNodeId, revision.ParagraphNodeId, revision.RunNodeId);

    private static HashSet<string> CommentTargets(DocumentCommentSnapshot comment) => Values(
        comment.ParagraphNodeId,
        comment.RunNodeId,
        comment.AnchorStartNodeId,
        comment.AnchorEndNodeId);

    private static HashSet<string> Values(params string?[] values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .ToHashSet(StringComparer.Ordinal);

    private static T[] Append<T>(IReadOnlyList<T> values, T value) where T : notnull => values
        .Append(value)
        .Distinct()
        .ToArray();

    private static string? Location(
        string? nodeId,
        Dictionary<string, string?> locations) =>
        nodeId is not null && locations.TryGetValue(nodeId, out var location) ? location : null;

    private static ComparisonFormatDifference? RevisionFormat(
        DocumentRevisionSnapshot revision,
        string? nodeId)
    {
        if (revision.PreviousCharacterFormatting is not null)
        {
            return new ComparisonFormatDifference(
                FormatDifferenceScope.Character,
                null,
                nodeId,
                [new FormatPropertyDifference("NativeRevisionPreviousFormat", "available", "current")]);
        }

        if (revision.PreviousParagraphFormatting is not null)
        {
            return new ComparisonFormatDifference(
                FormatDifferenceScope.Paragraph,
                null,
                nodeId,
                [new FormatPropertyDifference("NativeRevisionPreviousFormat", "available", "current")]);
        }

        return revision.PreviousTableFormatting is null
            ? null
            : new ComparisonFormatDifference(
                FormatDifferenceScope.Table,
                null,
                nodeId,
                [new FormatPropertyDifference("NativeRevisionPreviousFormat", "available", "current")]);
    }
}
