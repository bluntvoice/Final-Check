namespace FinalCheck.Core.Documents;

public sealed record FontFamilySnapshot(
    string? Ascii,
    string? HighAnsi,
    string? EastAsia,
    string? ComplexScript,
    string? AsciiTheme,
    string? HighAnsiTheme,
    string? EastAsiaTheme,
    string? ComplexScriptTheme)
{
    public static FontFamilySnapshot Empty { get; } = new(null, null, null, null, null, null, null, null);
}

public sealed record CharacterFormatSnapshot(
    FontFamilySnapshot Fonts,
    int? FontSizeHalfPoints,
    string? Color,
    bool? Bold,
    bool? Italic,
    string? Underline,
    bool? Strike,
    string? Highlight)
{
    public static CharacterFormatSnapshot Empty { get; } = new(
        FontFamilySnapshot.Empty,
        null,
        null,
        null,
        null,
        null,
        null,
        null);
}

public sealed record ParagraphFormatSnapshot(
    string? Alignment,
    string? LeftIndent,
    string? RightIndent,
    string? FirstLineIndent,
    string? HangingIndent,
    string? SpacingBefore,
    string? SpacingAfter,
    string? LineSpacing,
    string? LineRule)
{
    public static ParagraphFormatSnapshot Empty { get; } = new(null, null, null, null, null, null, null, null, null);
}

public sealed record TableBorderSnapshot(string? Style, string? Color, uint? Size);

public sealed record TableBordersSnapshot(
    TableBorderSnapshot? Top,
    TableBorderSnapshot? Left,
    TableBorderSnapshot? Bottom,
    TableBorderSnapshot? Right,
    TableBorderSnapshot? InsideHorizontal,
    TableBorderSnapshot? InsideVertical)
{
    public static TableBordersSnapshot Empty { get; } = new(null, null, null, null, null, null);
}

public sealed record TableFormatSnapshot(
    string? StyleId,
    string? Width,
    string? WidthType,
    string? Alignment,
    string? ShadingFill,
    TableBordersSnapshot Borders)
{
    public static TableFormatSnapshot Empty { get; } = new(null, null, null, null, null, TableBordersSnapshot.Empty);
}

public sealed record TableCellFormatSnapshot(
    string? Width,
    string? WidthType,
    string? VerticalAlignment,
    string? ShadingFill,
    int? GridSpan,
    string? VerticalMerge,
    TableBordersSnapshot Borders)
{
    public static TableCellFormatSnapshot Empty { get; } = new(null, null, null, null, null, null, TableBordersSnapshot.Empty);
}
