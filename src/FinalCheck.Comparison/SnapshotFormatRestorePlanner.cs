using System.Security.Cryptography;
using System.Text.Json;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Comparison;

/// <summary>Snapshot-only planning; the existing comparison is the sole mapping authority.</summary>
public sealed class SnapshotFormatRestorePlanner(ITextDiffService? textDiffService = null) : IFormatRestorePlanner
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
            AddCharacterItems(bp, cp, mapping, eligible, items, diagnostics, cancellationToken);
        }
        foreach (var paragraph in currentParagraphs.Values.Where(p => !mapped.Contains(p.NodeId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var neighbours = comparison.NodeMappings.Where(m => m.CurrentNode.Kind == DocumentNodeKind.Paragraph &&
                Trusted(m, policy) && currentParagraphs.ContainsKey(m.CurrentNode.NodeId) && baselineParagraphs.ContainsKey(m.BaselineNode.NodeId) &&
                currentParagraphs[m.CurrentNode.NodeId].Identity.ParentNodeId == paragraph.Identity.ParentNodeId).ToArray();
            var previous = neighbours.Where(m => currentParagraphs[m.CurrentNode.NodeId].Index < paragraph.Index)
                .OrderByDescending(m => currentParagraphs[m.CurrentNode.NodeId].Index).FirstOrDefault();
            var next = neighbours.Where(m => currentParagraphs[m.CurrentNode.NodeId].Index > paragraph.Index)
                .OrderBy(m => currentParagraphs[m.CurrentNode.NodeId].Index).FirstOrDefault();
            var candidates = new[] { previous, next }.OfType<ComparisonNodeMapping>()
                .Where(m => SameLevel(paragraph, currentParagraphs[m.CurrentNode.NodeId])).ToArray();
            var targets = candidates.Select(m => baselineParagraphs[m.BaselineNode.NodeId]).ToArray();
            if (targets.Length == 2 && targets[0].EffectiveFormatting == targets[1].EffectiveFormatting &&
                targets[0].StyleId == targets[1].StyleId && SameLevel(targets[0], targets[1]))
            {
                var bp = targets[0];
                AddItem(items, bp.NodeId, paragraph.Identity,
                    new(Paragraph: paragraph.EffectiveFormatting, ParagraphStyleId: paragraph.StyleId),
                    new(Paragraph: bp.EffectiveFormatting, ParagraphStyleId: bp.StyleId),
                    FormatRestoreCategory.Paragraph, true, candidates[0], "SameLevelMappedNeighbourConsensus");
                AddCharacterItems(bp, paragraph, candidates[0], true, items, diagnostics, cancellationToken);
                diagnostics.Add(new("AddedParagraphFallbackUsed", paragraph.NodeId, "Same-level trusted neighbours agree; original mapping is retained as evidence."));
            }
            else diagnostics.Add(new("UnmappedNode", paragraph.NodeId, "No reliable same-level neighbour consensus; no speculative restore."));
        }
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

    private void AddCharacterItems(DocumentParagraphSnapshot baseline, DocumentParagraphSnapshot current,
        ComparisonNodeMapping mapping, bool eligible, List<FormatRestoreItem> items,
        List<FormatRestoreDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var spans = (textDiffService ?? new TokenTextDiffService(new MixedLanguageTextTokenizer()))
            .Compare(baseline.DisplayText, current.DisplayText, cancellationToken);
        var baselineRuns = new List<(int Start, int End, DocumentRunSnapshot Run)>();
        var position = 0;
        foreach (var run in baseline.Runs)
        {
            if (run.DisplayText.Length > 0) baselineRuns.Add((position, position + run.DisplayText.Length, run));
            position += run.DisplayText.Length;
        }
        var uniform = baselineRuns.Select(r => r.Run.EffectiveFormatting).Distinct().ToArray();
        position = 0;
        foreach (var run in current.Runs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = position;
            position += run.DisplayText.Length;
            if (run.RawText.Length == 0) continue; // comment reference / drawing-only run
            var targets = new List<DocumentRunSnapshot>();
            var fallback = false;
            if (uniform.Length == 1) targets.Add(baselineRuns[0].Run);
            else if (run.DisplayText.Length > 0)
            {
                var cursor = start;
                while (cursor < position)
                {
                    var span = spans.FirstOrDefault(s => s.CurrentLength > 0 && cursor >= s.CurrentStart && cursor < s.CurrentStart + s.CurrentLength);
                    if (span is not null)
                    {
                        fallback |= span.BaselineLength == 0;
                        if (span.BaselineLength > 0)
                            targets.AddRange(baselineRuns.Where(r => r.Start < span.BaselineStart + span.BaselineLength && r.End > span.BaselineStart).Select(r => r.Run));
                        else
                        {
                            var left = baselineRuns.LastOrDefault(r => r.End <= span.BaselineStart);
                            var right = baselineRuns.FirstOrDefault(r => r.Start >= span.BaselineStart);
                            if (left.Run is not null) targets.Add(left.Run);
                            if (right.Run is not null) targets.Add(right.Run);
                        }
                        cursor = Math.Min(position, span.CurrentStart + span.CurrentLength);
                    }
                    else
                    {
                        var shift = spans.Where(s => s.CurrentStart + s.CurrentLength <= cursor).Sum(s => s.BaselineLength - s.CurrentLength);
                        var offset = cursor + shift;
                        var target = baselineRuns.FirstOrDefault(r => offset >= r.Start && offset < r.End);
                        if (target.Run is null) break;
                        targets.Add(target.Run);
                        cursor = Math.Min(position, Math.Min(target.End - shift,
                            spans.Where(s => s.CurrentLength > 0 && s.CurrentStart > cursor).Select(s => s.CurrentStart).DefaultIfEmpty(position).Min()));
                    }
                }
            }
            var formats = targets.Select(r => r.EffectiveFormatting).Distinct().ToArray();
            if (formats.Length != 1)
            {
                diagnostics.Add(new("MixedRunFormattingNeedsReview", run.NodeId,
                    "Run crosses conflicting target formats or deleted text lacks a reliable target; keep its XML intact."));
                continue;
            }
            fallback |= spans.Any(s => s.Operation == DifferenceOperation.Insert &&
                s.CurrentStart < position && s.CurrentStart + s.CurrentLength > start);
            var source = uniform.Length == 1 ? "MappedParagraphUniformFormat" : "MappedNeighbourRunConsensus";
            AddItem(items, targets[0].NodeId, run.Identity, new(Character: run.EffectiveFormatting),
                new(Character: formats[0]), FormatRestoreCategory.Character, eligible, mapping, fallback ? source : null);
            if (fallback) diagnostics.Add(new("AddedTextFallbackUsed", run.NodeId, source));
        }
    }

    internal static bool Trusted(ComparisonNodeMapping mapping, FormatRestorePolicy policy) =>
        mapping.Confidence is ComparisonConfidenceLevel.Exact or ComparisonConfidenceLevel.High &&
        double.IsFinite(mapping.Score) && mapping.Score >= policy.MinimumScore && mapping.Score <= 1;

    private static bool SameLevel(DocumentParagraphSnapshot left, DocumentParagraphSnapshot right) =>
        left.StyleId == right.StyleId && left.Numbering?.LevelIndex == right.Numbering?.LevelIndex &&
        (left.Numbering is null) == (right.Numbering is null);

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
