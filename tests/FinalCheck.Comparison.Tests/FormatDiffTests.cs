using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison.Tests;

public sealed class FormatDiffTests
{
    [Fact]
    public void FontSlotChangesAreGroupedInOneCharacterFormatChange()
    {
        var baselineFormat = Character(fontAscii: "Arial", fontEastAsia: "宋体");
        var currentFormat = Character(fontAscii: "Calibri", fontEastAsia: "仿宋");
        var result = CompareParagraphs("合同", "合同", baselineFormat, currentFormat);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ComparisonChangeKind.CharacterFormatChange, change.Kind);
        Assert.Equal(2, change.FormatDifference!.Properties.Count);
        Assert.Contains(change.FormatDifference.Properties, property => property.Property == "Font.Ascii");
        Assert.Contains(change.FormatDifference.Properties, property => property.Property == "Font.EastAsia");
    }

    [Fact]
    public void FontSizeChangeUsesEffectiveHalfPoints()
    {
        var result = CompareParagraphs(
            "合同",
            "合同",
            Character(fontSize: 21),
            Character(fontSize: 18));

        var property = Assert.Single(Assert.Single(result.Changes).FormatDifference!.Properties);
        Assert.Equal("FontSizeHalfPoints", property.Property);
        Assert.Equal("21", property.BaselineValue);
        Assert.Equal("18", property.CurrentValue);
    }

    [Fact]
    public void MultipleCharacterPropertiesRemainOneTopLevelChange()
    {
        var result = CompareParagraphs(
            "合同",
            "合同",
            Character(bold: false, italic: false, color: "000000"),
            Character(bold: true, italic: true, color: "FF0000"));

        var change = Assert.Single(result.Changes);
        Assert.Equal(3, change.FormatDifference!.Properties.Count);
    }

    [Fact]
    public void TextAndFormattingChangesRemainIndependentlyInspectable()
    {
        var result = CompareParagraphs(
            "付款期限为30日",
            "付款期限为60日",
            Character(bold: false),
            Character(bold: true));

        Assert.Contains(result.Changes, change => change.Kind == ComparisonChangeKind.TextReplace);
        Assert.Contains(result.Changes, change => change.Kind == ComparisonChangeKind.CharacterFormatChange);
    }

    [Fact]
    public void ParagraphFormattingUsesEffectiveValues()
    {
        var baseline = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec(
            "合同",
            ParagraphFormatting: new ParagraphFormatSnapshot("left", "0", null, "420", null, "0", "0", "240", "auto")));
        var current = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec(
            "合同",
            ParagraphFormatting: new ParagraphFormatSnapshot("both", "120", null, null, "240", "120", "160", "360", "exact")));

        var change = Assert.Single(ComparisonFixtureFactory.Engine().Compare(baseline, current).Changes);

        Assert.Equal(ComparisonChangeKind.ParagraphFormatChange, change.Kind);
        Assert.True(change.FormatDifference!.Properties.Count >= 7);
        Assert.Contains(change.FormatDifference.Properties, property => property.Property == "Alignment");
    }

    [Fact]
    public void CellFormattingCreatesOneCellChange()
    {
        var baseline = Table(Cell("金额", new TableCellFormatSnapshot(
            "2400", "dxa", "top", "FFFFFF", 1, null, TableBordersSnapshot.Empty)));
        var current = Table(Cell("金额", new TableCellFormatSnapshot(
            "3000", "dxa", "center", "FFFF00", 1, null, TableBordersSnapshot.Empty)));

        var change = Assert.Single(ComparisonFixtureFactory.Engine().Compare(baseline, current).Changes);

        Assert.Equal(ComparisonChangeKind.TableCellChange, change.Kind);
        Assert.Equal(3, change.FormatDifference!.Properties.Count);
    }

    [Fact]
    public void MergeStateChangeIsDetected()
    {
        var baseline = Table(Cell("合并", new TableCellFormatSnapshot(
            null, null, null, null, 2, "restart", TableBordersSnapshot.Empty)));
        var current = Table(Cell("合并", new TableCellFormatSnapshot(
            null, null, null, null, 1, null, TableBordersSnapshot.Empty)));

        var change = Assert.Single(ComparisonFixtureFactory.Engine().Compare(baseline, current).Changes);

        Assert.Contains(change.FormatDifference!.Properties, property => property.Property == "GridSpan");
        Assert.Contains(change.FormatDifference.Properties, property => property.Property == "VerticalMerge");
    }

    [Fact]
    public void CellTextAndFormatAreCombinedInOneCellChange()
    {
        var baseline = Table(Cell("30日", new TableCellFormatSnapshot(
            "1000", "dxa", null, null, null, null, TableBordersSnapshot.Empty)));
        var current = Table(Cell("60日", new TableCellFormatSnapshot(
            "1200", "dxa", null, null, null, null, TableBordersSnapshot.Empty)));

        var change = Assert.Single(ComparisonFixtureFactory.Engine().Compare(baseline, current).Changes);

        Assert.Equal("30", Assert.Single(change.DifferenceSpans).OldText);
        Assert.Equal("Width", Assert.Single(change.FormatDifference!.Properties).Property);
    }

    [Fact]
    public void TableStructureDifferenceReportsDiagnosticAndKeepsReliableCells()
    {
        var baseline = ComparisonFixtureFactory.WithTables(new ComparisonFixtureFactory.TableSpec(
        [
            new ComparisonFixtureFactory.RowSpec([Cell("A"), Cell("B")]),
        ]));
        var current = ComparisonFixtureFactory.WithTables(new ComparisonFixtureFactory.TableSpec(
        [
            new ComparisonFixtureFactory.RowSpec([Cell("A")]),
        ]));

        var result = ComparisonFixtureFactory.Engine().Compare(baseline, current);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TableStructureChanged");
        Assert.Contains(result.NodeMappings, mapping => mapping.BaselineNode.Kind == DocumentNodeKind.Cell);
    }

    private static ComparisonResult CompareParagraphs(
        string baselineText,
        string currentText,
        CharacterFormatSnapshot baselineFormat,
        CharacterFormatSnapshot currentFormat)
    {
        var baseline = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec(
            baselineText,
            CharacterFormatting: baselineFormat));
        var current = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec(
            currentText,
            CharacterFormatting: currentFormat));
        return ComparisonFixtureFactory.Engine().Compare(baseline, current);
    }

    private static CharacterFormatSnapshot Character(
        string? fontAscii = null,
        string? fontEastAsia = null,
        int? fontSize = null,
        string? color = null,
        bool? bold = null,
        bool? italic = null) => new(
        new FontFamilySnapshot(fontAscii, null, fontEastAsia, null, null, null, null, null),
        fontSize,
        color,
        bold,
        italic,
        null,
        null,
        null);

    private static ComparisonFixtureFactory.CellSpec Cell(
        string text,
        TableCellFormatSnapshot? format = null) => new(text, format);

    private static DocumentSnapshot Table(ComparisonFixtureFactory.CellSpec cell) =>
        ComparisonFixtureFactory.WithTables(new ComparisonFixtureFactory.TableSpec(
        [
            new ComparisonFixtureFactory.RowSpec([cell]),
        ]));
}
