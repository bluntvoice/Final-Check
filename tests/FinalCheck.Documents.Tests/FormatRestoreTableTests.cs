using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Comparison;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Documents.Tests;

public sealed class FormatRestoreTableTests
{
    private readonly OpenXmlDocumentParser _parser = new();
    private static Table Table(bool formatted, params string[] values) => new(new OpenXmlElement[] {
        formatted ? new TableProperties(new TableWidth { Width = "5000", Type = TableWidthUnitValues.Dxa }, new TableJustification { Val = TableRowAlignmentValues.Center }) : new TableProperties() }.Concat(
        values.Select(value => new TableRow(formatted ? new TableRowProperties(new TableRowHeight { Val = 500, HeightType = HeightRuleValues.AtLeast }) : new TableRowProperties(),
            new TableCell(formatted ? new TableCellProperties(new TableCellWidth { Width = "2000", Type = TableWidthUnitValues.Dxa },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }, new Shading { Fill = "D9EAF7" },
                new TableCellBorders(new TopBorder { Val = BorderValues.Single, Color = "336699", Size = 8 })) : new TableCellProperties(), new Paragraph(new Run(new Text(value))))))));

    [Fact]
    public async Task RestoresTableCellAndRowDirectFormattingWithoutChangingText()
    {
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(Table(true, "原文字")));
        using var current = FormatRestoreFixtureFactory.Create(new Body(Table(false, "修改后文字")));
        var b = await _parser.ParseAsync(baseline);
        var c = await _parser.ParseAsync(current);
        var plan = new SnapshotFormatRestorePlanner().Generate(b, c, FormatRestoreFixtureFactory.Engine().Compare(b, c));
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, plan);
        var table = Assert.Single(result.Snapshot.Tables);
        Assert.Equal("center", table.DirectFormatting.Alignment);
        Assert.Equal("5000", table.DirectFormatting.Width);
        Assert.Equal((uint)500, table.Rows[0].HeightTwips);
        Assert.Equal("修改后文字", table.Rows[0].Cells[0].DisplayText);
        Assert.Equal(b.Tables[0].Rows[0].Cells[0].DirectFormatting, table.Rows[0].Cells[0].DirectFormatting);
    }

    [Fact]
    public async Task ChangedStructureOnlyRestoresUniqueUnchangedMappedCell()
    {
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(Table(true, "唯一锚点", "旧内容")));
        using var current = FormatRestoreFixtureFactory.Create(new Body(Table(false, "唯一锚点", "外部变化", "新增行")));
        var b = await _parser.ParseAsync(baseline);
        var c = await _parser.ParseAsync(current);
        var plan = new SnapshotFormatRestorePlanner().Generate(b, c, FormatRestoreFixtureFactory.Engine().Compare(b, c));
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, plan);
        Assert.Equal(3, result.Snapshot.Tables[0].Rows.Count);
        Assert.Equal("D9EAF7", result.Snapshot.Tables[0].Rows[0].Cells[0].DirectFormatting.ShadingFill);
        Assert.Null(result.Snapshot.Tables[0].Rows[1].Cells[0].DirectFormatting.ShadingFill);
        Assert.Contains(result.Diagnostics, d => d.Code == "LowConfidenceMapping");
    }

    [Fact]
    public async Task MergeStateIsNeverRestored()
    {
        var table = Table(false, "锚点");
        table.Elements<TableRow>().Single().Elements<TableCell>().Single().TableCellProperties!.Append(new GridSpan { Val = 2 }, new VerticalMerge { Val = MergedCellValues.Restart });
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(Table(true, "锚点")));
        using var current = FormatRestoreFixtureFactory.Create(new Body(table));
        var b = await _parser.ParseAsync(baseline);
        var c = await _parser.ParseAsync(current);
        var plan = new SnapshotFormatRestorePlanner().Generate(b, c, FormatRestoreFixtureFactory.Engine().Compare(b, c));
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, plan);
        var cell = result.Snapshot.Tables[0].Rows[0].Cells[0];
        Assert.Equal(2, cell.DirectFormatting.GridSpan);
        Assert.Equal("restart", cell.DirectFormatting.VerticalMerge);
        Assert.Contains(plan.Diagnostics, d => d.Code == "MergeStructurePreserved");
    }

    [Fact]
    public async Task ConditionalTableStyleIsExplicitlyPartialAndRetained()
    {
        var bt = Table(true, "锚点"); bt.TableProperties!.TableStyle = new TableStyle { Val = "TemplateTable" };
        var ct = Table(false, "锚点"); ct.TableProperties!.TableStyle = new TableStyle { Val = "CurrentTable" };
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(bt), new Styles(new Style { StyleId = "TemplateTable", Type = StyleValues.Table }));
        using var current = FormatRestoreFixtureFactory.Create(new Body(ct), new Styles(new Style { StyleId = "CurrentTable", Type = StyleValues.Table }));
        var b = await _parser.ParseAsync(baseline); var c = await _parser.ParseAsync(current);
        var plan = new SnapshotFormatRestorePlanner().Generate(b, c, FormatRestoreFixtureFactory.Engine().Compare(b, c));
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, plan);
        Assert.Contains(plan.Diagnostics, d => d.Code == "UnsupportedTableStyle");
        Assert.Equal("CurrentTable", result.Snapshot.Tables[0].DirectFormatting.StyleId);
        using var document = WordprocessingDocument.Open(new MemoryStream(result.DocumentBytes), false);
        Assert.Equal("锚点", document.MainDocumentPart!.Document!.Descendants<Text>().Single().Text);
    }
}
