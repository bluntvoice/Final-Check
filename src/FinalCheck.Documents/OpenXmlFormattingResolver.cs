using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Core.Documents;

namespace FinalCheck.Documents;

internal sealed class OpenXmlFormattingResolver
{
    private readonly Dictionary<string, Style> _styles;
    private readonly string? _defaultParagraphStyleId;
    private readonly List<DocumentParseDiagnostic> _diagnostics;
    private readonly HashSet<string> _reportedDiagnostics = new(StringComparer.Ordinal);

    public OpenXmlFormattingResolver(Styles? styles, List<DocumentParseDiagnostic> diagnostics)
    {
        _diagnostics = diagnostics;
        _styles = styles?.Elements<Style>()
            .Where(style => !string.IsNullOrWhiteSpace(style.StyleId?.Value))
            .GroupBy(style => style.StyleId!.Value!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, Style>(StringComparer.Ordinal);
        _defaultParagraphStyleId = _styles.Values.FirstOrDefault(style =>
            style.Type?.Value == StyleValues.Paragraph && (style.Default?.Value ?? false))?.StyleId?.Value;

        var docDefaults = styles?.DocDefaults;
        Defaults = new DocumentDefaultsSnapshot(
            OpenXmlFormattingReader.ReadCharacterFormat(
                docDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle),
            OpenXmlFormattingReader.ReadParagraphFormat(
                docDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle));
        StyleSnapshots = _styles.Values
            .OrderBy(style => style.StyleId?.Value, StringComparer.Ordinal)
            .Select(ToSnapshot)
            .ToArray();
    }

    public DocumentDefaultsSnapshot Defaults { get; }

    public IReadOnlyList<DocumentStyleSnapshot> StyleSnapshots { get; }

    public ParagraphFormatSnapshot ResolveParagraph(Paragraph paragraph, string? nodeId, string sourcePart)
    {
        var effective = Defaults.ParagraphFormatting;
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? _defaultParagraphStyleId;
        foreach (var style in GetStyleChain(styleId, DocumentStyleKind.Paragraph, nodeId, sourcePart))
        {
            effective = OpenXmlFormattingReader.Merge(effective, OpenXmlFormattingReader.ReadParagraphFormat(style.StyleParagraphProperties));
        }

        return OpenXmlFormattingReader.Merge(
            effective,
            OpenXmlFormattingReader.ReadParagraphFormat(paragraph.ParagraphProperties));
    }

    public CharacterFormatSnapshot ResolveRun(Paragraph paragraph, Run run, string? nodeId, string sourcePart)
    {
        var effective = Defaults.CharacterFormatting;
        var paragraphStyleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? _defaultParagraphStyleId;
        foreach (var style in GetStyleChain(paragraphStyleId, DocumentStyleKind.Paragraph, nodeId, sourcePart))
        {
            effective = OpenXmlFormattingReader.MergeStyle(
                effective,
                OpenXmlFormattingReader.ReadCharacterFormat(style.StyleRunProperties));
        }

        var runStyleId = run.RunProperties?.RunStyle?.Val?.Value;
        foreach (var style in GetStyleChain(runStyleId, DocumentStyleKind.Character, nodeId, sourcePart))
        {
            effective = OpenXmlFormattingReader.MergeStyle(
                effective,
                OpenXmlFormattingReader.ReadCharacterFormat(style.StyleRunProperties));
        }

        effective = OpenXmlFormattingReader.Merge(
            effective,
            OpenXmlFormattingReader.ReadCharacterFormat(
                paragraph.ParagraphProperties?.ParagraphMarkRunProperties));
        return OpenXmlFormattingReader.Merge(
            effective,
            OpenXmlFormattingReader.ReadCharacterFormat(run.RunProperties));
    }

    private List<Style> GetStyleChain(
        string? styleId,
        DocumentStyleKind expectedKind,
        string? nodeId,
        string sourcePart)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return [];
        }

        var chain = new List<Style>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var currentId = styleId;
        while (!string.IsNullOrWhiteSpace(currentId))
        {
            if (!visited.Add(currentId))
            {
                ReportOnce(
                    "StyleInheritanceCycle:" + currentId,
                    new DocumentParseDiagnostic(
                        DocumentDiagnosticSeverity.Warning,
                        "StyleInheritanceCycle",
                        $"Style inheritance contains a cycle at '{currentId}'.",
                        nodeId,
                        sourcePart,
                        false));
                break;
            }

            if (!_styles.TryGetValue(currentId, out var style))
            {
                ReportOnce(
                    "BrokenStyleReference:" + currentId,
                    new DocumentParseDiagnostic(
                        DocumentDiagnosticSeverity.Warning,
                        "BrokenStyleReference",
                        $"Style '{currentId}' could not be resolved.",
                        nodeId,
                        sourcePart,
                        false));
                break;
            }

            var actualKind = GetStyleKind(style.Type?.Value);
            if (actualKind != expectedKind)
            {
                ReportOnce(
                    $"InvalidStyleType:{currentId}:{expectedKind}",
                    new DocumentParseDiagnostic(
                        DocumentDiagnosticSeverity.Warning,
                        "InvalidStyleType",
                        $"Style '{currentId}' is {actualKind}, not {expectedKind}; the reference was ignored.",
                        nodeId,
                        sourcePart,
                        false));
                break;
            }

            chain.Add(style);
            currentId = style.BasedOn?.Val?.Value;
        }

        chain.Reverse();
        return chain;
    }

    private DocumentStyleSnapshot ToSnapshot(Style style) => new(
        style.StyleId?.Value ?? string.Empty,
        style.StyleName?.Val?.Value,
        GetStyleKind(style.Type?.Value),
        style.BasedOn?.Val?.Value,
        style.LinkedStyle?.Val?.Value,
        style.Default?.Value ?? false,
        OpenXmlFormattingReader.ReadCharacterFormat(style.StyleRunProperties),
        OpenXmlFormattingReader.ReadParagraphFormat(style.StyleParagraphProperties),
        OpenXmlFormattingReader.ReadTableFormat(style.StyleTableProperties));

    private void ReportOnce(string key, DocumentParseDiagnostic diagnostic)
    {
        if (_reportedDiagnostics.Add(key))
        {
            _diagnostics.Add(diagnostic);
        }
    }

    private static DocumentStyleKind GetStyleKind(StyleValues? value)
    {
        if (value is null)
        {
            return DocumentStyleKind.Unknown;
        }

        if (value.Value == StyleValues.Paragraph)
        {
            return DocumentStyleKind.Paragraph;
        }

        if (value.Value == StyleValues.Character)
        {
            return DocumentStyleKind.Character;
        }

        if (value.Value == StyleValues.Table)
        {
            return DocumentStyleKind.Table;
        }

        if (value.Value == StyleValues.Numbering)
        {
            return DocumentStyleKind.Numbering;
        }

        return DocumentStyleKind.Unknown;
    }
}

internal static class OpenXmlFormattingReader
{
    public static CharacterFormatSnapshot ReadCharacterFormat(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return CharacterFormatSnapshot.Empty;
        }

        var fonts = properties.GetFirstChild<RunFonts>();
        var size = properties.GetFirstChild<FontSize>()?.Val?.Value;
        return new CharacterFormatSnapshot(
            fonts is null
                ? FontFamilySnapshot.Empty
                : new FontFamilySnapshot(
                    fonts.Ascii?.Value,
                    fonts.HighAnsi?.Value,
                    fonts.EastAsia?.Value,
                    fonts.ComplexScript?.Value,
                    fonts.AsciiTheme?.ToString(),
                    fonts.HighAnsiTheme?.ToString(),
                    fonts.EastAsiaTheme?.ToString(),
                    fonts.ComplexScriptTheme?.ToString()),
            int.TryParse(size, out var halfPoints) ? halfPoints : null,
            properties.GetFirstChild<Color>()?.Val?.Value,
            ReadOnOff<Bold>(properties),
            ReadOnOff<Italic>(properties),
            properties.GetFirstChild<Underline>()?.Val?.ToString(),
            ReadOnOff<Strike>(properties),
            properties.GetFirstChild<Highlight>()?.Val?.ToString());
    }

    public static ParagraphFormatSnapshot ReadParagraphFormat(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return ParagraphFormatSnapshot.Empty;
        }

        var indentation = properties.GetFirstChild<Indentation>();
        var spacing = properties.GetFirstChild<SpacingBetweenLines>();
        return new ParagraphFormatSnapshot(
            properties.GetFirstChild<Justification>()?.Val?.ToString(),
            indentation?.Left?.Value,
            indentation?.Right?.Value,
            indentation?.FirstLine?.Value,
            indentation?.Hanging?.Value,
            spacing?.Before?.Value,
            spacing?.After?.Value,
            spacing?.Line?.Value,
            spacing?.LineRule?.ToString());
    }

    public static TableFormatSnapshot ReadTableFormat(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return TableFormatSnapshot.Empty;
        }

        var width = properties.GetFirstChild<TableWidth>();
        return new TableFormatSnapshot(
            properties.GetFirstChild<TableStyle>()?.Val?.Value,
            width?.Width?.Value,
            width?.Type?.ToString(),
            properties.GetFirstChild<TableJustification>()?.Val?.ToString(),
            properties.GetFirstChild<Shading>()?.Fill?.Value,
            ReadBorders(properties.GetFirstChild<TableBorders>()));
    }

    public static TableCellFormatSnapshot ReadTableCellFormat(TableCellProperties? properties)
    {
        if (properties is null)
        {
            return TableCellFormatSnapshot.Empty;
        }

        var width = properties.TableCellWidth;
        return new TableCellFormatSnapshot(
            width?.Width?.Value,
            width?.Type?.ToString(),
            properties.TableCellVerticalAlignment?.Val?.ToString(),
            properties.Shading?.Fill?.Value,
            properties.GridSpan?.Val?.Value,
            properties.VerticalMerge?.Val?.ToString() ??
                (properties.VerticalMerge is null ? null : "continue"),
            ReadBorders(properties.TableCellBorders));
    }

    public static CharacterFormatSnapshot Merge(CharacterFormatSnapshot inherited, CharacterFormatSnapshot overlay) => new(
        Merge(inherited.Fonts, overlay.Fonts),
        overlay.FontSizeHalfPoints ?? inherited.FontSizeHalfPoints,
        overlay.Color ?? inherited.Color,
        overlay.Bold ?? inherited.Bold,
        overlay.Italic ?? inherited.Italic,
        overlay.Underline ?? inherited.Underline,
        overlay.Strike ?? inherited.Strike,
        overlay.Highlight ?? inherited.Highlight);

    public static CharacterFormatSnapshot MergeStyle(CharacterFormatSnapshot inherited, CharacterFormatSnapshot overlay) => new(
        Merge(inherited.Fonts, overlay.Fonts),
        overlay.FontSizeHalfPoints ?? inherited.FontSizeHalfPoints,
        overlay.Color ?? inherited.Color,
        ApplyToggle(inherited.Bold, overlay.Bold),
        ApplyToggle(inherited.Italic, overlay.Italic),
        overlay.Underline ?? inherited.Underline,
        ApplyToggle(inherited.Strike, overlay.Strike),
        overlay.Highlight ?? inherited.Highlight);

    public static ParagraphFormatSnapshot Merge(ParagraphFormatSnapshot inherited, ParagraphFormatSnapshot overlay) => new(
        overlay.Alignment ?? inherited.Alignment,
        overlay.LeftIndent ?? inherited.LeftIndent,
        overlay.RightIndent ?? inherited.RightIndent,
        overlay.FirstLineIndent ?? inherited.FirstLineIndent,
        overlay.HangingIndent ?? inherited.HangingIndent,
        overlay.SpacingBefore ?? inherited.SpacingBefore,
        overlay.SpacingAfter ?? inherited.SpacingAfter,
        overlay.LineSpacing ?? inherited.LineSpacing,
        overlay.LineRule ?? inherited.LineRule);

    private static FontFamilySnapshot Merge(FontFamilySnapshot inherited, FontFamilySnapshot overlay) => new(
        overlay.Ascii ?? inherited.Ascii,
        overlay.HighAnsi ?? inherited.HighAnsi,
        overlay.EastAsia ?? inherited.EastAsia,
        overlay.ComplexScript ?? inherited.ComplexScript,
        overlay.AsciiTheme ?? inherited.AsciiTheme,
        overlay.HighAnsiTheme ?? inherited.HighAnsiTheme,
        overlay.EastAsiaTheme ?? inherited.EastAsiaTheme,
        overlay.ComplexScriptTheme ?? inherited.ComplexScriptTheme);

    private static bool? ApplyToggle(bool? inherited, bool? overlay)
    {
        if (overlay is null || overlay == false)
        {
            return inherited;
        }

        return !(inherited ?? false);
    }

    private static bool? ReadOnOff<T>(OpenXmlElement properties)
        where T : OnOffType
    {
        var value = properties.GetFirstChild<T>();
        return value is null ? null : value.Val?.Value ?? true;
    }

    private static TableBordersSnapshot ReadBorders(OpenXmlElement? borders)
    {
        if (borders is null)
        {
            return TableBordersSnapshot.Empty;
        }

        return new TableBordersSnapshot(
            ReadBorder(borders.GetFirstChild<TopBorder>()),
            ReadBorder(borders.GetFirstChild<LeftBorder>()),
            ReadBorder(borders.GetFirstChild<BottomBorder>()),
            ReadBorder(borders.GetFirstChild<RightBorder>()),
            ReadBorder(borders.GetFirstChild<InsideHorizontalBorder>()),
            ReadBorder(borders.GetFirstChild<InsideVerticalBorder>()));
    }

    private static TableBorderSnapshot? ReadBorder(BorderType? border) => border is null
        ? null
        : new TableBorderSnapshot(border.Val?.ToString(), border.Color?.Value, border.Size?.Value);
}
