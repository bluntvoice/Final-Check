using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public sealed class EffectiveFormatDiffService : IFormatDiffService
{
    public IReadOnlyList<ComparisonFormatDifference> CompareParagraph(
        DocumentParagraphSnapshot baseline,
        DocumentParagraphSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var differences = new List<ComparisonFormatDifference>();
        var characterProperties = CharacterProperties(baseline, current);
        if (characterProperties.Count > 0)
        {
            differences.Add(new ComparisonFormatDifference(
                FormatDifferenceScope.Character,
                baseline.NodeId,
                current.NodeId,
                characterProperties));
        }

        var paragraphProperties = CompareProperties(
            ("Alignment", baseline.EffectiveFormatting.Alignment, current.EffectiveFormatting.Alignment),
            ("LeftIndent", baseline.EffectiveFormatting.LeftIndent, current.EffectiveFormatting.LeftIndent),
            ("RightIndent", baseline.EffectiveFormatting.RightIndent, current.EffectiveFormatting.RightIndent),
            ("FirstLineIndent", baseline.EffectiveFormatting.FirstLineIndent, current.EffectiveFormatting.FirstLineIndent),
            ("HangingIndent", baseline.EffectiveFormatting.HangingIndent, current.EffectiveFormatting.HangingIndent),
            ("SpacingBefore", baseline.EffectiveFormatting.SpacingBefore, current.EffectiveFormatting.SpacingBefore),
            ("SpacingAfter", baseline.EffectiveFormatting.SpacingAfter, current.EffectiveFormatting.SpacingAfter),
            ("LineSpacing", baseline.EffectiveFormatting.LineSpacing, current.EffectiveFormatting.LineSpacing),
            ("LineRule", baseline.EffectiveFormatting.LineRule, current.EffectiveFormatting.LineRule));
        if (paragraphProperties.Count > 0)
        {
            differences.Add(new ComparisonFormatDifference(
                FormatDifferenceScope.Paragraph,
                baseline.NodeId,
                current.NodeId,
                paragraphProperties));
        }

        return differences;
    }

    public ComparisonFormatDifference? CompareTable(
        DocumentTableSnapshot baseline,
        DocumentTableSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var properties = CompareProperties(
            ("StyleId", baseline.DirectFormatting.StyleId, current.DirectFormatting.StyleId),
            ("Width", baseline.DirectFormatting.Width, current.DirectFormatting.Width),
            ("WidthType", baseline.DirectFormatting.WidthType, current.DirectFormatting.WidthType),
            ("Alignment", baseline.DirectFormatting.Alignment, current.DirectFormatting.Alignment),
            ("ShadingFill", baseline.DirectFormatting.ShadingFill, current.DirectFormatting.ShadingFill),
            ("Borders", Borders(baseline.DirectFormatting.Borders), Borders(current.DirectFormatting.Borders)));
        return properties.Count == 0
            ? null
            : new ComparisonFormatDifference(
                FormatDifferenceScope.Table,
                baseline.NodeId,
                current.NodeId,
                properties);
    }

    public ComparisonFormatDifference? CompareCell(
        DocumentTableCellSnapshot baseline,
        DocumentTableCellSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var properties = CompareProperties(
            ("Width", baseline.DirectFormatting.Width, current.DirectFormatting.Width),
            ("WidthType", baseline.DirectFormatting.WidthType, current.DirectFormatting.WidthType),
            ("VerticalAlignment", baseline.DirectFormatting.VerticalAlignment, current.DirectFormatting.VerticalAlignment),
            ("ShadingFill", baseline.DirectFormatting.ShadingFill, current.DirectFormatting.ShadingFill),
            ("GridSpan", Value(baseline.DirectFormatting.GridSpan), Value(current.DirectFormatting.GridSpan)),
            ("VerticalMerge", baseline.DirectFormatting.VerticalMerge, current.DirectFormatting.VerticalMerge),
            ("Borders", Borders(baseline.DirectFormatting.Borders), Borders(current.DirectFormatting.Borders)));
        return properties.Count == 0
            ? null
            : new ComparisonFormatDifference(
                FormatDifferenceScope.TableCell,
                baseline.NodeId,
                current.NodeId,
                properties);
    }

    private static List<FormatPropertyDifference> CharacterProperties(
        DocumentParagraphSnapshot baseline,
        DocumentParagraphSnapshot current) => CompareProperties(
        CharacterProperty("Font.Ascii", baseline, current, format => format.Fonts.Ascii),
        CharacterProperty("Font.HighAnsi", baseline, current, format => format.Fonts.HighAnsi),
        CharacterProperty("Font.EastAsia", baseline, current, format => format.Fonts.EastAsia),
        CharacterProperty("Font.ComplexScript", baseline, current, format => format.Fonts.ComplexScript),
        CharacterProperty("FontSizeHalfPoints", baseline, current, format => Value(format.FontSizeHalfPoints)),
        CharacterProperty("Color", baseline, current, format => format.Color),
        CharacterProperty("Bold", baseline, current, format => Value(format.Bold)),
        CharacterProperty("Italic", baseline, current, format => Value(format.Italic)),
        CharacterProperty("Underline", baseline, current, format => format.Underline),
        CharacterProperty("Strike", baseline, current, format => Value(format.Strike)),
        CharacterProperty("Highlight", baseline, current, format => format.Highlight));

    private static (string Name, string? Baseline, string? Current) CharacterProperty(
        string name,
        DocumentParagraphSnapshot baseline,
        DocumentParagraphSnapshot current,
        Func<CharacterFormatSnapshot, string?> selector) =>
        (name, CharacterPattern(baseline, selector), CharacterPattern(current, selector));

    private static string CharacterPattern(
        DocumentParagraphSnapshot paragraph,
        Func<CharacterFormatSnapshot, string?> selector)
    {
        var values = paragraph.Runs
            .Where(run => run.DisplayText.Length > 0)
            .Select(run => (Length: run.DisplayText.Length, Value: selector(run.EffectiveFormatting) ?? "<null>"))
            .ToArray();
        if (values.Length == 0)
        {
            return "<null>";
        }

        if (values.Select(item => item.Value).Distinct(StringComparer.Ordinal).Count() == 1)
        {
            return values[0].Value;
        }

        var merged = new List<(int Length, string Value)>();
        foreach (var item in values)
        {
            if (merged.Count > 0 && string.Equals(merged[^1].Value, item.Value, StringComparison.Ordinal))
            {
                merged[^1] = (merged[^1].Length + item.Length, item.Value);
            }
            else
            {
                merged.Add(item);
            }
        }

        return string.Join("|", merged.Select(item => $"{item.Length}:{item.Value}"));
    }

    private static List<FormatPropertyDifference> CompareProperties(
        params (string Name, string? Baseline, string? Current)[] properties) => properties
        .Where(property => !string.Equals(property.Baseline, property.Current, StringComparison.Ordinal))
        .Select(property => new FormatPropertyDifference(property.Name, property.Baseline, property.Current))
        .ToList();

    private static string? Value<T>(T? value) where T : struct => value?.ToString();

    private static string Borders(TableBordersSnapshot borders) => string.Join(
        ";",
        Border("top", borders.Top),
        Border("left", borders.Left),
        Border("bottom", borders.Bottom),
        Border("right", borders.Right),
        Border("insideH", borders.InsideHorizontal),
        Border("insideV", borders.InsideVertical));

    private static string Border(string name, TableBorderSnapshot? border) => border is null
        ? $"{name}=<null>"
        : $"{name}={border.Style},{border.Color},{border.Size}";
}
