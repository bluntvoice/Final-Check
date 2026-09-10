using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public sealed class MultiSignalParagraphMatcher : IStructureMatcher
{
    private const double MinimumAcceptedScore = 0.62;
    private const double AmbiguityTolerance = 0.025;
    private const int PositionWindow = 4;
    private const int MaximumCandidates = 32;

    public ParagraphMatchResult Match(
        DocumentSnapshot baseline,
        DocumentSnapshot current,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var baselineFeatures = baseline.Paragraphs.Select(CreateFeatures).ToArray();
        var currentFeatures = current.Paragraphs.Select(CreateFeatures).ToArray();
        var baselineAvailable = baselineFeatures.Select(feature => feature.Paragraph.NodeId).ToHashSet(StringComparer.Ordinal);
        var currentAvailable = currentFeatures.Select(feature => feature.Paragraph.NodeId).ToHashSet(StringComparer.Ordinal);
        var mappings = new List<ComparisonNodeMapping>();
        var diagnostics = new List<ComparisonDiagnostic>();

        MatchStructuralExact(
            baselineFeatures,
            currentFeatures,
            baselineAvailable,
            currentAvailable,
            mappings,
            cancellationToken);
        MatchUniqueExact(
            baselineFeatures,
            currentFeatures,
            baselineAvailable,
            currentAvailable,
            mappings,
            cancellationToken);
        MatchRepeatedExact(
            baselineFeatures,
            currentFeatures,
            baselineAvailable,
            currentAvailable,
            mappings,
            diagnostics,
            cancellationToken);
        MatchSimilar(
            baselineFeatures,
            currentFeatures,
            baselineAvailable,
            currentAvailable,
            mappings,
            diagnostics,
            cancellationToken);

        return new ParagraphMatchResult(
            mappings.OrderBy(mapping => mapping.BaselineNode.Position).ToArray(),
            baselineFeatures
                .Where(feature => baselineAvailable.Contains(feature.Paragraph.NodeId))
                .Select(feature => feature.Paragraph)
                .ToArray(),
            currentFeatures
                .Where(feature => currentAvailable.Contains(feature.Paragraph.NodeId))
                .Select(feature => feature.Paragraph)
                .ToArray(),
            diagnostics);
    }

    private static void MatchStructuralExact(
        IReadOnlyList<ParagraphFeatures> baseline,
        IReadOnlyList<ParagraphFeatures> current,
        HashSet<string> baselineAvailable,
        HashSet<string> currentAvailable,
        List<ComparisonNodeMapping> mappings,
        CancellationToken cancellationToken)
    {
        var count = Math.Min(baseline.Count, current.Count);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baselineFeature = baseline[index];
            var currentFeature = current[index];
            if (!string.Equals(baselineFeature.NormalizedText, currentFeature.NormalizedText, StringComparison.Ordinal))
            {
                continue;
            }

            AddMapping(
                baselineFeature,
                currentFeature,
                ComparisonMappingType.Structural,
                ComparisonConfidenceLevel.Exact,
                1,
                [
                    new ComparisonMatchEvidence(ComparisonEvidenceKind.StructuralPath, 1, "Same paragraph index."),
                    new ComparisonMatchEvidence(ComparisonEvidenceKind.NormalizedText, 1, "Normalized text is identical."),
                ],
                baselineAvailable,
                currentAvailable,
                mappings);
        }
    }

    private static void MatchUniqueExact(
        IReadOnlyList<ParagraphFeatures> baseline,
        IReadOnlyList<ParagraphFeatures> current,
        HashSet<string> baselineAvailable,
        HashSet<string> currentAvailable,
        List<ComparisonNodeMapping> mappings,
        CancellationToken cancellationToken)
    {
        var baselineGroups = Available(baseline, baselineAvailable)
            .GroupBy(feature => feature.NormalizedText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var currentGroups = Available(current, currentAvailable)
            .GroupBy(feature => feature.NormalizedText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var (text, baselineGroup) in baselineGroups.OrderBy(item => item.Value[0].Paragraph.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (baselineGroup.Length != 1 || !currentGroups.TryGetValue(text, out var currentGroup) || currentGroup.Length != 1)
            {
                continue;
            }

            AddMapping(
                baselineGroup[0],
                currentGroup[0],
                ComparisonMappingType.ExactText,
                ComparisonConfidenceLevel.Exact,
                1,
                [new ComparisonMatchEvidence(ComparisonEvidenceKind.ExactText, 1, "Unique normalized text match.")],
                baselineAvailable,
                currentAvailable,
                mappings);
        }
    }

    private static void MatchRepeatedExact(
        IReadOnlyList<ParagraphFeatures> baseline,
        IReadOnlyList<ParagraphFeatures> current,
        HashSet<string> baselineAvailable,
        HashSet<string> currentAvailable,
        List<ComparisonNodeMapping> mappings,
        List<ComparisonDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var baselineGroups = Available(baseline, baselineAvailable)
            .GroupBy(feature => feature.NormalizedText, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Paragraph.Index).ToArray(), StringComparer.Ordinal);
        var currentGroups = Available(current, currentAvailable)
            .GroupBy(feature => feature.NormalizedText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Paragraph.Index).ToArray(), StringComparer.Ordinal);
        foreach (var (text, baselineGroup) in baselineGroups.OrderBy(item => item.Value[0].Paragraph.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!currentGroups.TryGetValue(text, out var currentGroup))
            {
                continue;
            }

            var pairCount = Math.Min(baselineGroup.Length, currentGroup.Length);
            diagnostics.Add(new ComparisonDiagnostic(
                ComparisonDiagnosticSeverity.Info,
                "DuplicateCandidate",
                $"Repeated normalized paragraph text produced {baselineGroup.Length} baseline and {currentGroup.Length} current candidates; stable document order was used.",
                baselineGroup[0].Paragraph.NodeId,
                currentGroup[0].Paragraph.NodeId));
            if (baselineGroup.Length != currentGroup.Length)
            {
                diagnostics.Add(new ComparisonDiagnostic(
                    ComparisonDiagnosticSeverity.Warning,
                    "AmbiguousParagraphMatch",
                    "Repeated paragraph counts differ; only deterministic order-preserving pairs were mapped.",
                    baselineGroup[0].Paragraph.NodeId,
                    currentGroup[0].Paragraph.NodeId));
            }

            for (var index = 0; index < pairCount; index++)
            {
                AddMapping(
                    baselineGroup[index],
                    currentGroup[index],
                    ComparisonMappingType.Contextual,
                    ComparisonConfidenceLevel.Medium,
                    0.72,
                    [
                        new ComparisonMatchEvidence(ComparisonEvidenceKind.ExactText, 1, "Repeated normalized text is identical."),
                        new ComparisonMatchEvidence(ComparisonEvidenceKind.Context, 0.44, "Stable order resolves duplicate candidates."),
                    ],
                    baselineAvailable,
                    currentAvailable,
                    mappings);
            }
        }
    }

    private static void MatchSimilar(
        IReadOnlyList<ParagraphFeatures> baseline,
        IReadOnlyList<ParagraphFeatures> current,
        HashSet<string> baselineAvailable,
        HashSet<string> currentAvailable,
        List<ComparisonNodeMapping> mappings,
        List<ComparisonDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var currentByToken = BuildTokenIndex(Available(current, currentAvailable));
        var proposals = new List<MatchProposal>();
        foreach (var baselineFeature in Available(baseline, baselineAvailable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = CandidateIndexes(baselineFeature, current, currentAvailable, currentByToken);
            var scored = candidates
                .Select(index => CreateProposal(baselineFeature, current[index], baseline, current))
                .OrderByDescending(proposal => proposal.Score)
                .ThenBy(proposal => Math.Abs(proposal.Baseline.Paragraph.Index - proposal.Current.Paragraph.Index))
                .ThenBy(proposal => proposal.Current.Paragraph.Index)
                .ToArray();
            if (scored.Length == 0)
            {
                continue;
            }

            if (scored[0].Score < MinimumAcceptedScore)
            {
                diagnostics.Add(new ComparisonDiagnostic(
                    ComparisonDiagnosticSeverity.Info,
                    "LowConfidenceMapping",
                    $"Best paragraph candidate scored {scored[0].Score:F3} and was left unmatched.",
                    baselineFeature.Paragraph.NodeId,
                    scored[0].Current.Paragraph.NodeId));
                continue;
            }

            if (scored.Length > 1 && scored[0].Score - scored[1].Score <= AmbiguityTolerance)
            {
                diagnostics.Add(new ComparisonDiagnostic(
                    ComparisonDiagnosticSeverity.Warning,
                    "AmbiguousParagraphMatch",
                    $"Two paragraph candidates scored within {AmbiguityTolerance:F3}; the baseline paragraph was left unmatched.",
                    baselineFeature.Paragraph.NodeId,
                    null));
                continue;
            }

            proposals.Add(scored[0]);
        }

        foreach (var proposal in proposals
                     .OrderByDescending(item => item.Score)
                     .ThenBy(item => item.Baseline.Paragraph.Index)
                     .ThenBy(item => item.Current.Paragraph.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!baselineAvailable.Contains(proposal.Baseline.Paragraph.NodeId) ||
                !currentAvailable.Contains(proposal.Current.Paragraph.NodeId))
            {
                continue;
            }

            AddMapping(
                proposal.Baseline,
                proposal.Current,
                proposal.MappingType,
                proposal.Score >= 0.82 ? ComparisonConfidenceLevel.High : ComparisonConfidenceLevel.Medium,
                proposal.Score,
                proposal.Evidence,
                baselineAvailable,
                currentAvailable,
                mappings);
        }
    }

    private static Dictionary<string, List<int>> BuildTokenIndex(IEnumerable<ParagraphFeatures> features)
    {
        var index = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var feature in features)
        {
            foreach (var token in feature.Tokens)
            {
                if (!index.TryGetValue(token, out var positions))
                {
                    positions = [];
                    index[token] = positions;
                }

                positions.Add(feature.Paragraph.Index);
            }
        }

        return index;
    }

    private static int[] CandidateIndexes(
        ParagraphFeatures baseline,
        IReadOnlyList<ParagraphFeatures> current,
        HashSet<string> currentAvailable,
        Dictionary<string, List<int>> currentByToken)
    {
        var candidates = new HashSet<int>();
        var start = Math.Max(0, baseline.Paragraph.Index - PositionWindow);
        var end = Math.Min(current.Count - 1, baseline.Paragraph.Index + PositionWindow);
        for (var index = start; index <= end; index++)
        {
            if (currentAvailable.Contains(current[index].Paragraph.NodeId))
            {
                candidates.Add(index);
            }
        }

        foreach (var token in baseline.Tokens.OrderBy(token => currentByToken.TryGetValue(token, out var items) ? items.Count : int.MaxValue))
        {
            if (!currentByToken.TryGetValue(token, out var positions))
            {
                continue;
            }

            foreach (var position in positions)
            {
                if (currentAvailable.Contains(current[position].Paragraph.NodeId))
                {
                    candidates.Add(position);
                }
            }

            if (candidates.Count >= MaximumCandidates)
            {
                break;
            }
        }

        if (baseline.HeadingKey is not null)
        {
            foreach (var feature in current.Where(feature =>
                         currentAvailable.Contains(feature.Paragraph.NodeId) &&
                         string.Equals(feature.HeadingKey, baseline.HeadingKey, StringComparison.Ordinal)))
            {
                candidates.Add(feature.Paragraph.Index);
            }
        }

        return candidates
            .OrderBy(index => Math.Abs(index - baseline.Paragraph.Index))
            .ThenBy(index => index)
            .Take(MaximumCandidates)
            .ToArray();
    }

    private static MatchProposal CreateProposal(
        ParagraphFeatures baseline,
        ParagraphFeatures current,
        IReadOnlyList<ParagraphFeatures> baselineDocument,
        IReadOnlyList<ParagraphFeatures> currentDocument)
    {
        var textScore = TextSimilarity(baseline.NormalizedText, current.NormalizedText);
        var maximumCount = Math.Max(baselineDocument.Count, currentDocument.Count);
        var positionScore = maximumCount <= 1
            ? 1
            : 1 - ((double)Math.Abs(baseline.Paragraph.Index - current.Paragraph.Index) / maximumCount);
        var styleScore = string.Equals(baseline.Paragraph.StyleId, current.Paragraph.StyleId, StringComparison.Ordinal)
            ? baseline.Paragraph.StyleId is null ? 0.35 : 1
            : 0;
        var numberingScore = NumberingScore(baseline.Paragraph.Numbering, current.Paragraph.Numbering);
        var headingScore = baseline.HeadingKey is not null &&
                           string.Equals(baseline.HeadingKey, current.HeadingKey, StringComparison.Ordinal)
            ? 1
            : 0;
        var contextScore = ContextScore(baseline, current, baselineDocument, currentDocument);
        var structuralScore = 0.70 * textScore +
                              0.10 * positionScore +
                              0.05 * styleScore +
                              0.05 * Math.Max(numberingScore, headingScore) +
                              0.10 * contextScore;
        var evidence = new List<ComparisonMatchEvidence>
        {
            new(ComparisonEvidenceKind.TextSimilarity, textScore, "Token and character n-gram similarity."),
            new(ComparisonEvidenceKind.Position, positionScore, "Relative paragraph position."),
        };
        if (styleScore > 0)
        {
            evidence.Add(new ComparisonMatchEvidence(ComparisonEvidenceKind.Style, styleScore, baseline.Paragraph.StyleId));
        }

        if (numberingScore > 0)
        {
            evidence.Add(new ComparisonMatchEvidence(ComparisonEvidenceKind.Numbering, numberingScore, "Numbering reference compatibility."));
        }

        if (headingScore > 0)
        {
            evidence.Add(new ComparisonMatchEvidence(ComparisonEvidenceKind.Heading, headingScore, baseline.HeadingKey));
        }

        if (contextScore > 0)
        {
            evidence.Add(new ComparisonMatchEvidence(ComparisonEvidenceKind.Context, contextScore, "Neighbour text consistency."));
        }

        var mappingType = Math.Max(numberingScore, headingScore) > 0
            ? ComparisonMappingType.HeadingOrNumbering
            : contextScore >= 0.5
                ? ComparisonMappingType.Contextual
                : ComparisonMappingType.SimilarText;
        return new MatchProposal(baseline, current, Math.Round(structuralScore, 6), mappingType, evidence);
    }

    private static double ContextScore(
        ParagraphFeatures baseline,
        ParagraphFeatures current,
        IReadOnlyList<ParagraphFeatures> baselineDocument,
        IReadOnlyList<ParagraphFeatures> currentDocument)
    {
        var comparisons = 0;
        var matches = 0;
        if (baseline.Paragraph.Index > 0 && current.Paragraph.Index > 0)
        {
            comparisons++;
            if (string.Equals(
                    baselineDocument[baseline.Paragraph.Index - 1].NormalizedText,
                    currentDocument[current.Paragraph.Index - 1].NormalizedText,
                    StringComparison.Ordinal))
            {
                matches++;
            }
        }

        if (baseline.Paragraph.Index + 1 < baselineDocument.Count && current.Paragraph.Index + 1 < currentDocument.Count)
        {
            comparisons++;
            if (string.Equals(
                    baselineDocument[baseline.Paragraph.Index + 1].NormalizedText,
                    currentDocument[current.Paragraph.Index + 1].NormalizedText,
                    StringComparison.Ordinal))
            {
                matches++;
            }
        }

        return comparisons == 0 ? 0 : (double)matches / comparisons;
    }

    private static double NumberingScore(
        ParagraphNumberingReferenceSnapshot? baseline,
        ParagraphNumberingReferenceSnapshot? current)
    {
        if (baseline is null || current is null)
        {
            return 0;
        }

        if (baseline.LevelIndex != current.LevelIndex)
        {
            return 0;
        }

        return string.Equals(baseline.LevelText, current.LevelText, StringComparison.Ordinal) &&
               string.Equals(baseline.NumberFormat, current.NumberFormat, StringComparison.Ordinal)
            ? 1
            : 0.55;
    }

    private static double TextSimilarity(string baseline, string current)
    {
        if (string.Equals(baseline, current, StringComparison.Ordinal))
        {
            return 1;
        }

        if (baseline.Length == 0 || current.Length == 0)
        {
            return 0;
        }

        var baselineTokens = ParagraphTextNormalizer.Tokenize(baseline);
        var currentTokens = ParagraphTextNormalizer.Tokenize(current);
        var tokenUnion = baselineTokens.Union(currentTokens, StringComparer.Ordinal).Count();
        var tokenIntersection = baselineTokens.Intersect(currentTokens, StringComparer.Ordinal).Count();
        var tokenScore = tokenUnion == 0 ? 0 : (double)tokenIntersection / tokenUnion;
        var ngramScore = BigramDice(baseline, current);
        var lengthScore = (double)Math.Min(baseline.Length, current.Length) / Math.Max(baseline.Length, current.Length);
        return (0.45 * ngramScore) + (0.35 * tokenScore) + (0.20 * lengthScore);
    }

    private static double BigramDice(string baseline, string current)
    {
        if (baseline.Length == 1 || current.Length == 1)
        {
            return baseline[0] == current[0] ? 1 : 0;
        }

        var baselineBigrams = Bigrams(baseline);
        var currentBigrams = Bigrams(current);
        var intersection = baselineBigrams.Intersect(currentBigrams, StringComparer.Ordinal).Count();
        return (2d * intersection) / (baselineBigrams.Count + currentBigrams.Count);
    }

    private static HashSet<string> Bigrams(string text)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < text.Length - 1; index++)
        {
            values.Add(text.Substring(index, 2));
        }

        return values;
    }

    private static IEnumerable<ParagraphFeatures> Available(
        IEnumerable<ParagraphFeatures> features,
        HashSet<string> available) => features.Where(feature => available.Contains(feature.Paragraph.NodeId));

    private static ParagraphFeatures CreateFeatures(DocumentParagraphSnapshot paragraph)
    {
        var normalized = ParagraphTextNormalizer.Normalize(paragraph.DisplayText);
        return new ParagraphFeatures(
            paragraph,
            normalized,
            ParagraphTextNormalizer.Tokenize(normalized),
            ParagraphTextNormalizer.GetHeadingKey(normalized));
    }

    private static void AddMapping(
        ParagraphFeatures baseline,
        ParagraphFeatures current,
        ComparisonMappingType mappingType,
        ComparisonConfidenceLevel confidence,
        double score,
        IReadOnlyList<ComparisonMatchEvidence> evidence,
        HashSet<string> baselineAvailable,
        HashSet<string> currentAvailable,
        List<ComparisonNodeMapping> mappings)
    {
        baselineAvailable.Remove(baseline.Paragraph.NodeId);
        currentAvailable.Remove(current.Paragraph.NodeId);
        mappings.Add(new ComparisonNodeMapping(
            Reference(baseline.Paragraph),
            Reference(current.Paragraph),
            mappingType,
            confidence,
            score,
            evidence,
            false,
            !string.Equals(baseline.NormalizedText, current.NormalizedText, StringComparison.Ordinal)));
    }

    private static DocumentNodeReference Reference(DocumentParagraphSnapshot paragraph) => new(
        paragraph.NodeId,
        DocumentNodeKind.Paragraph,
        paragraph.Index,
        paragraph.Identity.StructuralPath);

    private sealed record ParagraphFeatures(
        DocumentParagraphSnapshot Paragraph,
        string NormalizedText,
        IReadOnlySet<string> Tokens,
        string? HeadingKey);

    private sealed record MatchProposal(
        ParagraphFeatures Baseline,
        ParagraphFeatures Current,
        double Score,
        ComparisonMappingType MappingType,
        IReadOnlyList<ComparisonMatchEvidence> Evidence);
}
