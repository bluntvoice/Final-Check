using System.Text.RegularExpressions;

namespace FinalCheck.Core.Comparisons;

/// <summary>Per-comparison presentation policy. The frozen ComparisonResult is never rewritten.</summary>
public sealed record ComparisonIgnoreRules(
    bool Punctuation = false,
    bool PageNumbers = false,
    bool Numbering = false,
    string Characters = "",
    bool AllFormatting = false,
    IReadOnlyList<string>? FormatProperties = null)
{
    public IReadOnlyList<string> HiddenProperties => FormatProperties ?? [];
    public bool IsEmpty => !Punctuation && !PageNumbers && !Numbering && Characters.Length == 0 && !AllFormatting && HiddenProperties.Count == 0;
}

public static partial class ComparisonIgnoreProjection
{
    private const string PunctuationCharacters = "，。；：！？（）【】《》“”‘’、,.;:!?()[]\"'";

    public static ComparisonChangeItem? Project(ComparisonChangeItem raw, ComparisonIgnoreRules rules)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.IsEmpty) return raw;
        var spans = raw.DifferenceSpans.Where(span => !HideTextSpan(raw, span, rules)).ToArray();
        var format = raw.FormatDifference is null ? null : raw.FormatDifference with
        {
            Properties = raw.FormatDifference.Properties.Where(property => !HideFormat(raw.FormatDifference.Scope, property.Property, rules)).ToArray()
        };
        if (format is { Properties.Count: 0 }) format = null;
        if (spans.Length == 0 && format is null && raw.DifferenceSpans.Count + (raw.FormatDifference?.Properties.Count ?? 0) > 0 &&
            raw.Kind is not (ComparisonChangeKind.ParagraphMove or ComparisonChangeKind.ParagraphMoveAndModify) &&
            raw.RevisionIds.Count == 0 && raw.CommentIds.Count == 0 && raw.DiagnosticCodes.Count == 0)
            return null;
        return raw with { DifferenceSpans = spans, FormatDifference = format };
    }

    private static bool HideFormat(FormatDifferenceScope scope, string property, ComparisonIgnoreRules rules)
    {
        // Merge topology is structural evidence, never a presentation-only format property.
        if (property is "GridSpan" or "VerticalMerge" or "TableStructureChanged") return false;
        return rules.AllFormatting || rules.HiddenProperties.Contains($"{scope}.{property}", StringComparer.Ordinal);
    }

    private static bool HideTextSpan(ComparisonChangeItem item, DifferenceSpan span, ComparisonIgnoreRules rules)
    {
        if (rules.IsEmpty) return false;
        var oldText = span.OldText;
        var newText = span.NewText;
        if (rules.PageNumbers && PageMarker().IsMatch(item.BaselineText.Trim()) && PageMarker().IsMatch(item.CurrentText.Trim())) return true;
        if (rules.Numbering)
        {
            var before = NumberPrefix().Match(item.BaselineText);
            var after = NumberPrefix().Match(item.CurrentText);
            if (before.Success && after.Success && span.BaselineStart + span.BaselineLength <= before.Length &&
                span.CurrentStart + span.CurrentLength <= after.Length) return true;
        }
        var ignored = (rules.Punctuation ? PunctuationCharacters : "") + rules.Characters;
        if (ignored.Length == 0) return false;
        static string Remove(string value, string ignored) => new(value.Where(c => !ignored.Contains(c)).ToArray());
        return !string.Equals(oldText, newText, StringComparison.Ordinal) &&
            string.Equals(Remove(oldText, ignored), Remove(newText, ignored), StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^(?:第\s*\d+\s*页|Page\s+\d+|\d+\s*/\s*\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PageMarker();
    [GeneratedRegex(@"^\s*(?:\d+[.、)]|[（(]\d+[）)]|[①-⑳]|[A-Za-z][.)]|[一二三四五六七八九十]+、|（[一二三四五六七八九十]+）)(?=\s|\p{L}|\p{N}|$)", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPrefix();
}
