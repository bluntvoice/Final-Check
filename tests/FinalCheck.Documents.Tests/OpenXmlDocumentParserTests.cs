using System.Diagnostics;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Core.Documents;
using FinalCheck.Documents;
using Xunit.Abstractions;

namespace FinalCheck.Documents.Tests;

public sealed class OpenXmlDocumentParserTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ParseReadsContentFormattingTablesRevisionsAndComments()
    {
        await using var stream = CreateFixture();
        var parser = new OpenXmlDocumentParser();
        var serializer = new JsonDocumentSnapshotSerializer();
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var stopwatch = Stopwatch.StartNew();

        var snapshot = await parser.ParseAsync(stream);

        stopwatch.Stop();
        var allocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(true) - allocatedBefore);
        var payload = serializer.Serialize(snapshot);
        var roundTrip = serializer.Deserialize(payload);
        output.WriteLine("DOCX_PARSE_ELAPSED_MS={0:F3}", stopwatch.Elapsed.TotalMilliseconds);
        output.WriteLine("DOCX_PARSE_ALLOCATED_BYTES={0}", allocatedBytes);
        output.WriteLine("SNAPSHOT_PAYLOAD_BYTES={0}", payload.Length);

        var firstParagraph = Assert.Single(snapshot.Paragraphs.Take(1));
        Assert.Equal("第一段第二个 Run", firstParagraph.Text);
        Assert.NotNull(firstParagraph.Format.Alignment);
        Assert.Equal("240", firstParagraph.Format.LeftIndent);
        Assert.Equal("480", firstParagraph.Format.FirstLineIndent);
        Assert.Collection(
            firstParagraph.Runs,
            firstRun =>
            {
                Assert.Equal("第一段", firstRun.Text);
                Assert.Equal("Calibri", firstRun.Format.FontFamily);
                Assert.Equal(24, firstRun.Format.FontSizeHalfPoints);
                Assert.Equal("336699", firstRun.Format.Color);
                Assert.True(firstRun.Format.IsBold);
            },
            secondRun => Assert.Equal("第二个 Run", secondRun.Text));
        var table = Assert.Single(snapshot.Tables);
        var row = Assert.Single(table.Rows);
        Assert.Equal(["单元格 A", "单元格 B"], row.Cells.Select(cell => cell.Text));
        Assert.Contains(snapshot.Revisions, revision => revision.Kind == DocumentRevisionKind.Insert && revision.Text == "插入内容");
        Assert.Contains(snapshot.Revisions, revision => revision.Kind == DocumentRevisionKind.Delete && revision.Text == "删除内容");
        Assert.Contains(snapshot.Comments, comment => comment.Text == "测试批注");
        Assert.Equal(snapshot.SnapshotSchemaVersion, roundTrip.SnapshotSchemaVersion);
        Assert.Equal(payload, serializer.Serialize(roundTrip));
    }

    [Fact]
    public async Task ParseDisposesPackageAndLeavesSourceStreamUsable()
    {
        await using var stream = CreateFixture();
        var parser = new OpenXmlDocumentParser();

        _ = await parser.ParseAsync(stream);

        Assert.True(stream.CanRead);
        stream.Position = 0;
        Assert.NotEqual(-1, stream.ReadByte());
    }

    private static MemoryStream CreateFixture()
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            body.AppendChild(new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = JustificationValues.Left },
                    new Indentation { Left = "240", FirstLine = "480" }),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" },
                        new FontSize { Val = "24" },
                        new Color { Val = "336699" },
                        new Bold()),
                    new Text("第一段")),
                new Run(new Text("第二个 Run"))));

            body.AppendChild(new Paragraph(
                new InsertedRun(
                    new Run(new Text("插入内容")))
                {
                    Id = "1",
                    Author = "Fixture",
                    Date = DateTime.UtcNow,
                },
                new DeletedRun(
                    new Run(new DeletedText("删除内容")))
                {
                    Id = "2",
                    Author = "Fixture",
                    Date = DateTime.UtcNow,
                },
                new CommentRangeStart { Id = "0" },
                new Run(new Text("带批注文本")),
                new CommentRangeEnd { Id = "0" },
                new Run(new CommentReference { Id = "0" })));

            body.AppendChild(new Table(
                new TableRow(
                    CreateCell("单元格 A", "D9EAF7"),
                    CreateCell("单元格 B", "FFFFFF"))));

            var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
            commentsPart.Comments = new Comments(
                new Comment(
                    new Paragraph(new Run(new Text("测试批注"))))
                {
                    Id = "0",
                    Author = "Fixture",
                    Date = DateTime.UtcNow,
                });
            commentsPart.Comments.Save();
            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private static TableCell CreateCell(string text, string fill) => new(
        new TableCellProperties(
            new TableCellWidth { Width = "2400", Type = TableWidthUnitValues.Dxa },
            new Shading { Fill = fill },
            new GridSpan { Val = 1 }),
        new Paragraph(new Run(new Text(text))));
}
