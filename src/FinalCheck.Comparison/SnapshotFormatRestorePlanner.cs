using System.Security.Cryptography;
using System.Text.Json;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Comparison;

/// <summary>Snapshot-only planning; the existing comparison is the sole mapping authority.</summary>
public sealed class SnapshotFormatRestorePlanner : IFormatRestorePlanner
{
    public FormatRestorePlan Generate(
        DocumentSnapshot baseline, DocumentSnapshot current, ComparisonResult comparison,
        FormatRestorePolicy? policy = null, DateTimeOffset? createdAt = null,
        IProgress<FormatRestoreProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(comparison);
        policy ??= new FormatRestorePolicy();
        if (!double.IsFinite(policy.MinimumScore) || policy.MinimumScore is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(policy));
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(FormatRestoreStage.Preparing, 0, null));
        if (baseline.SnapshotSchemaVersion != DocumentSnapshot.CurrentSchemaVersion ||
            current.SnapshotSchemaVersion != DocumentSnapshot.CurrentSchemaVersion ||
            comparison.ComparisonSchemaVersion != ComparisonResult.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported restore input schema.");
        CheckIdentity(baseline, comparison.Metadata.BaselineSnapshotId);
        CheckIdentity(current, comparison.Metadata.CurrentSnapshotId);
        var baselineParagraphs = Paragraphs(baseline).ToDictionary(p => p.NodeId, StringComparer.Ordinal);
        var currentParagraphs = Paragraphs(current).ToDictionary(p => p.NodeId, StringComparer.Ordinal);
        var items = new List<FormatRestoreItem>();
        var diagnostics = new List<FormatRestoreDiagnostic>();
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        progress?.Report(new(FormatRestoreStage.ResolvingMappings, 0, comparison.NodeMappings.Count));
        foreach (var mapping in comparison.NodeMappings.OrderBy(m => m.CurrentNode.NodeId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mapping.CurrentNode.Kind != DocumentNodeKind.Paragraph) continue;
            if (!baselineParagraphs.TryGetValue(mapping.BaselineNode.NodeId, out var bp) ||
                !currentParagraphs.TryGetValue(mapping.CurrentNode.NodeId, out var cp) || !mapped.Add(cp.NodeId))
                throw new InvalidDataException("Invalid or duplicate paragraph mapping.");
            var eligible = Trusted(mapping, policy);
            if (!eligible) diagnostics.Add(new("LowConfidenceMapping", cp.NodeId, "Mapping is not trusted for automatic restore."));
            AddItem(items, bp.NodeId, cp.Identity, new(Paragraph: cp.EffectiveFormatting, ParagraphStyleId: cp.StyleId),
                new(Paragraph: bp.EffectiveFormatting, ParagraphStyleId: bp.StyleId), FormatRestoreCategory.Paragraph,
                eligible, mapping, null);
            var formats = bp.Runs.Where(r => !r.IsEmpty).Select(r => r.EffectiveFormatting).Distinct().ToArray();
            if (formats.Length == 1)
            {
                foreach (var run in cp.Runs.Where(r => r.RawText.Length > 0))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddItem(items, bp.NodeId, run.Identity, new(Character: run.EffectiveFormatting),
                        new(Character: formats[0]), FormatRestoreCategory.Character, eligible, mapping,
                        bp.DisplayText == cp.DisplayText ? null : "MappedParagraphUniformFormat");
                }
            }
            else if (formats.Length > 1)
                diagnostics.Add(new("MixedRunFormattingNeedsReview", cp.NodeId, "A reliable text-position projection is required."));
        }
        foreach (var paragraph in currentParagraphs.Values.Where(p => !mapped.Contains(p.NodeId)))
            diagnostics.Add(new("UnmappedNode", paragraph.NodeId, "No comparison mapping; no speculative restore."));
        progress?.Report(new(FormatRestoreStage.ValidatingPlan, items.Count, items.Count));
        var orderedItems = items.OrderBy(i => i.RestoreItemId, StringComparer.Ordinal).ToArray();
        var orderedDiagnostics = diagnostics.OrderBy(d => d.NodeId, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToArray();
        var mappingReference = Hash(comparison);
        var planId = Hash(new { baseline, current, comparison, policy });
        progress?.Report(new(FormatRestoreStage.Completed, items.Count, items.Count));
        return new(FormatRestorePlan.CurrentSchemaVersion, planId, comparison.Metadata.CurrentSnapshotId,
            comparison.Metadata.BaselineSnapshotId, comparison.Metadata.CurrentSnapshotId, current.Metadata.Sha256,
            mappingReference, createdAt ?? current.Metadata.ModifiedUtc ?? DateTimeOffset.UnixEpoch,
            policy, orderedItems, orderedDiagnostics,
            items.Any(i => i.Eligibility != FormatRestoreEligibility.Eligible) || diagnostics.Count > 0
                ? FormatRestorePlanStatus.NeedsReview : items.Count == 0 ? FormatRestorePlanStatus.Empty : FormatRestorePlanStatus.Ready);
    }

    internal static IEnumerable<DocumentParagraphSnapshot> Paragraphs(DocumentSnapshot snapshot) =>
        snapshot.Paragraphs.Concat(snapshot.Tables.SelectMany(t => t.Rows).SelectMany(r => r.Cells).SelectMany(c => c.Paragraphs));

    internal static bool Trusted(ComparisonNodeMapping mapping, FormatRestorePolicy policy) =>
        mapping.Confidence is ComparisonConfidenceLevel.Exact or ComparisonConfidenceLevel.High &&
        double.IsFinite(mapping.Score) && mapping.Score >= policy.MinimumScore && mapping.Score <= 1;

    internal static void AddItem(List<FormatRestoreItem> items, string? baselineId,
        DocumentNodeIdentitySnapshot identity, RestoreFormatting current, RestoreFormatting target,
        FormatRestoreCategory category, bool eligible, ComparisonNodeMapping? mapping, string? fallback)
    {
        var differences = Differences(current, target);
        if (differences.Count == 0) return;
        var diagnostic = !eligible ? new[] { new FormatRestoreDiagnostic("LowConfidenceMapping", identity.NodeId, "Requires review; automatic execution is forbidden.") } : [];
        items.Add(new(Hash(new { baselineId, identity.NodeId, category, current, target }), baselineId,
            identity.NodeId, identity.Kind, identity, current, target, differences, category,
            eligible ? FormatRestoreEligibility.Eligible : FormatRestoreEligibility.NeedsReview,
            fallback, mapping?.Score ?? 0, mapping, diagnostic));
    }

    internal static IReadOnlyList<string> Differences(RestoreFormatting current, RestoreFormatting target)
    {
        using var left = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(current));
        using var right = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(target));
        var result = new List<string>();
        Walk(left.RootElement, right.RootElement, "", result);
        return result;
    }

    private static void Walk(JsonElement left, JsonElement right, string prefix, List<string> result)
    {
        if (left.ValueKind == JsonValueKind.Object && right.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in left.EnumerateObject())
                Walk(property.Value, right.GetProperty(property.Name), prefix.Length == 0 ? property.Name : prefix + "." + property.Name, result);
        }
        else if (left.GetRawText() != right.GetRawText()) result.Add(prefix);
    }

    private static void CheckIdentity(DocumentSnapshot snapshot, string identity)
    {
        if (snapshot.Metadata.Sha256.Length > 0 && identity != "sha256:" + snapshot.Metadata.Sha256)
            throw new InvalidDataException("Comparison does not belong to the supplied snapshot.");
    }

    internal static string Hash<T>(T value) => Convert.ToHexString(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
}
