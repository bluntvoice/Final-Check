using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Documents.Tests;

public sealed class FormatRestoreSafetyTests
{
    private static readonly string[] FormatRevisionKinds = ["pPrChange", "rPrChange", "tblPrChange", "trPrChange", "tcPrChange"];
    [Fact]
    public async Task RestoreAndUndoPreserveAllFiveExistingFormatRevisionKindsAndTextAnnotations()
    {
        var w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var current = FormatRestoreFixtureFactory.Create(new Body($"""
            <w:body xmlns:w="{w}">
              <w:p><w:pPr><w:pPrChange w:id="11" w:author="Fixture"><w:pPr><w:jc w:val="right"/></w:pPr></w:pPrChange></w:pPr>
                <w:commentRangeStart w:id="0"/><w:r><w:rPr><w:rPrChange w:id="12" w:author="Fixture"><w:rPr><w:i/></w:rPr></w:rPrChange></w:rPr><w:t>正文</w:t></w:r>
                <w:ins w:id="1" w:author="Fixture"><w:r><w:t>新增60日</w:t></w:r></w:ins><w:del w:id="2" w:author="Fixture"><w:r><w:delText>删除30日</w:delText></w:r></w:del>
                <w:commentRangeEnd w:id="0"/><w:r><w:commentReference w:id="0"/></w:r></w:p>
              <w:tbl><w:tblPr><w:tblPrChange w:id="13" w:author="Fixture"><w:tblPr><w:tblW w:w="2000" w:type="dxa"/></w:tblPr></w:tblPrChange></w:tblPr><w:tblGrid><w:gridCol w:w="3000"/></w:tblGrid>
                <w:tr><w:trPr><w:trPrChange w:id="14" w:author="Fixture"><w:trPr><w:trHeight w:val="200"/></w:trPr></w:trPrChange></w:trPr>
                  <w:tc><w:tcPr><w:tcPrChange w:id="15" w:author="Fixture"><w:tcPr><w:shd w:fill="FF0000"/></w:tcPr></w:tcPrChange></w:tcPr><w:p><w:r><w:t>表格正文</w:t></w:r></w:p></w:tc>
                </w:tr></w:tbl>
            </w:body>
            """), comments: new Comments(new Comment(new Paragraph(new Run(new Text("批注保持")))) { Id = "0", Author = "Fixture" }), trackChanges: true);
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(
            new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }), new Run(new RunProperties(new Bold()), new Text("正文新增60日"))),
            new Table(new TableProperties(new TableWidth { Width = "3000", Type = TableWidthUnitValues.Dxa }), new TableGrid(new GridColumn { Width = "3000" }),
                new TableRow(new TableRowProperties(new TableRowHeight { Val = 400 }), new TableCell(new TableCellProperties(new Shading { Fill = "CCCCCC" }), new Paragraph(new Run(new Text("表格正文"))))))));
        var parser = new OpenXmlDocumentParser();
        var before = await parser.ParseAsync(current);
        var plan = FormatRestoreFixtureFactory.Plan(await parser.ParseAsync(baseline), before);
        var renderer = new OpenXmlFormatRestoreRenderer(parser);
        var restored = await renderer.RenderAsync(current, plan);
        using var restoredInput = new MemoryStream(restored.DocumentBytes);
        var undone = await renderer.RevertAsync(restoredInput, restored.Snapshot.Metadata.Sha256, restored.Mutations);
        Assert.NotEmpty(restored.Mutations);
        foreach (var result in new[] { restored, undone })
        {
            Assert.Equal(JsonSerializer.Serialize(before.Revisions), JsonSerializer.Serialize(result.Snapshot.Revisions));
            Assert.Equal(JsonSerializer.Serialize(before.Comments), JsonSerializer.Serialize(result.Snapshot.Comments));
            Assert.Equal(before.Paragraphs[0].RawText, result.Snapshot.Paragraphs[0].RawText);
            using var package = WordprocessingDocument.Open(new MemoryStream(result.DocumentBytes), false);
            var changes = package.MainDocumentPart!.Document!.Descendants().Where(e => e.LocalName.EndsWith("PrChange", StringComparison.Ordinal)).ToArray();
            Assert.Equal(5, changes.Length);
            Assert.Equal(FormatRevisionKinds.Order(), changes.Select(e => e.LocalName).Order());
            Assert.Single(package.MainDocumentPart.Document.Descendants<InsertedRun>());
            Assert.Single(package.MainDocumentPart.Document.Descendants<DeletedRun>());
            Assert.True(package.MainDocumentPart.DocumentSettingsPart!.Settings!.GetFirstChild<TrackRevisions>() is not null);
        }
        Assert.Equal(before.Paragraphs[0].EffectiveFormatting, undone.Snapshot.Paragraphs[0].EffectiveFormatting);
    }

    [Fact]
    public async Task SelectedItemsOnlyModifyChosenNodeAndRejectForgedLowConfidenceEligibility()
    {
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }), new Run(new RunProperties(new Bold()), new Text("合同")))));
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new Text("合同")))));
        var parser = new OpenXmlDocumentParser();
        var plan = FormatRestoreFixtureFactory.Plan(await parser.ParseAsync(baseline), await parser.ParseAsync(current));
        var character = plan.RestoreItems.Single(i => i.Category == FormatRestoreCategory.Character);
        var renderer = new OpenXmlFormatRestoreRenderer(parser);
        var result = await renderer.RenderAsync(current, plan, new(FormatRestoreScopeKind.SelectedItems, SelectedItemIds: [character.RestoreItemId]));
        Assert.True(result.Snapshot.Paragraphs[0].Runs[0].EffectiveFormatting.Bold);
        Assert.Null(result.Snapshot.Paragraphs[0].EffectiveFormatting.Alignment);
        Assert.Single(result.Mutations);
        var forged = character with { Mapping = character.Mapping! with { Confidence = ComparisonConfidenceLevel.Low } };
        await Assert.ThrowsAsync<InvalidDataException>(async () => await renderer.RenderAsync(current, plan with { RestoreItems = [forged] }));
    }

    [Fact]
    public async Task RowPropertiesKeepSchemaOrderAroundUnhandledTablePropertyExceptions()
    {
        Body TableBody(bool formatted) => new($"""
            <w:body xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:tbl>
            <w:tblPr/><w:tblGrid><w:gridCol w:w="3000"/></w:tblGrid><w:tr>
            <w:tblPrEx><w:tblW w:w="2500" w:type="dxa"/></w:tblPrEx>
            {(formatted ? "<w:trPr><w:trHeight w:val=\"400\"/></w:trPr>" : "")}
            <w:tc><w:tcPr/><w:p><w:r><w:t>表格正文</w:t></w:r></w:p></w:tc>
            </w:tr></w:tbl></w:body>
            """);
        using var baseline = FormatRestoreFixtureFactory.Create(TableBody(true));
        using var current = FormatRestoreFixtureFactory.Create(TableBody(false));
        var parser = new OpenXmlDocumentParser(); var renderer = new OpenXmlFormatRestoreRenderer(parser);
        var b = await parser.ParseAsync(baseline); var c = await parser.ParseAsync(current);
        var result = await renderer.RenderAsync(current, FormatRestoreFixtureFactory.Plan(b, c));
        using var package = WordprocessingDocument.Open(new MemoryStream(result.DocumentBytes), false);
        var row = package.MainDocumentPart!.Document!.Descendants<TableRow>().Single();
        Assert.Equal("tblPrEx", row.ChildElements[0].LocalName);
        Assert.Equal("trPr", row.ChildElements[1].LocalName);
        using var input = new MemoryStream(result.DocumentBytes);
        var undo = await renderer.RevertAsync(input, result.Snapshot.Metadata.Sha256, result.Mutations);
        Assert.Null(undo.Snapshot.Tables[0].Rows[0].HeightTwips);
    }
}
