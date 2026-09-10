using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Documents;

namespace FinalCheck.Documents;

public sealed class OpenXmlDocumentParser : IDocumentParser
{
    public ValueTask<DocumentSnapshot> ParseAsync(
        Stream docxStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(docxStream);
        if (!docxStream.CanRead || !docxStream.CanSeek)
        {
            throw new ArgumentException("The DOCX stream must be readable and seekable.", nameof(docxStream));
        }

        cancellationToken.ThrowIfCancellationRequested();
        docxStream.Position = 0;

        using var document = WordprocessingDocument.Open(docxStream, false);
        var mainPart = document.MainDocumentPart
            ?? throw new InvalidDataException("The DOCX package has no main document part.");
        var mainDocument = mainPart.Document
            ?? throw new InvalidDataException("The DOCX main document part is empty.");
        var body = mainDocument.Body
            ?? throw new InvalidDataException("The DOCX main document has no body.");

        var paragraphs = body.Elements<Paragraph>()
            .Select((paragraph, index) => ParseParagraph(paragraph, index))
            .ToArray();
        var tables = body.Elements<Table>()
            .Select((table, index) => ParseTable(table, index))
            .ToArray();
        var revisions = ParseRevisions(body);
        var comments = ParseComments(mainPart);

        return ValueTask.FromResult(new DocumentSnapshot(
            DocumentSnapshot.CurrentSchemaVersion,
            paragraphs,
            tables,
            revisions,
            comments));
    }

    private static DocumentParagraphSnapshot ParseParagraph(Paragraph paragraph, int index)
    {
        var runs = paragraph.Descendants<Run>()
            .Select((run, runIndex) => ParseRun(run, index, runIndex))
            .ToArray();
        var properties = paragraph.ParagraphProperties;

        return new DocumentParagraphSnapshot(
            $"paragraph:{index}",
            index,
            string.Concat(runs.Select(run => run.Text)),
            runs,
            new ParagraphFormatSnapshot(
                properties?.ParagraphStyleId?.Val?.Value,
                properties?.Justification?.Val?.Value.ToString(),
                properties?.Indentation?.Left?.Value,
                properties?.Indentation?.FirstLine?.Value));
    }

    private static DocumentRunSnapshot ParseRun(Run run, int paragraphIndex, int runIndex)
    {
        var properties = run.RunProperties;
        var text = string.Concat(run.ChildElements.Select(GetRunChildText));
        int? fontSize = int.TryParse(properties?.FontSize?.Val?.Value, out var halfPoints)
            ? halfPoints
            : null;

        return new DocumentRunSnapshot(
            $"paragraph:{paragraphIndex}/run:{runIndex}",
            runIndex,
            text,
            new CharacterFormatSnapshot(
                properties?.RunFonts?.Ascii?.Value ?? properties?.RunFonts?.HighAnsi?.Value,
                fontSize,
                properties?.Color?.Val?.Value,
                properties?.Bold is not null,
                properties?.Italic is not null,
                properties?.Underline?.Val?.Value.ToString()));
    }

    private static string GetRunChildText(DocumentFormat.OpenXml.OpenXmlElement element) => element switch
    {
        Text text => text.Text,
        DeletedText deletedText => deletedText.Text,
        TabChar => "\t",
        Break => Environment.NewLine,
        _ => string.Empty,
    };

    private static DocumentTableSnapshot ParseTable(Table table, int index)
    {
        var rows = table.Elements<TableRow>()
            .Select((row, rowIndex) => new DocumentTableRowSnapshot(
                $"table:{index}/row:{rowIndex}",
                rowIndex,
                row.Elements<TableCell>()
                    .Select((cell, cellIndex) => ParseCell(cell, index, rowIndex, cellIndex))
                    .ToArray()))
            .ToArray();

        return new DocumentTableSnapshot($"table:{index}", index, rows);
    }

    private static DocumentTableCellSnapshot ParseCell(
        TableCell cell,
        int tableIndex,
        int rowIndex,
        int cellIndex)
    {
        var properties = cell.TableCellProperties;
        return new DocumentTableCellSnapshot(
            $"table:{tableIndex}/row:{rowIndex}/cell:{cellIndex}",
            cellIndex,
            string.Join(Environment.NewLine, cell.Elements<Paragraph>().Select(p => p.InnerText)),
            properties?.TableCellWidth?.Width?.Value,
            properties?.Shading?.Fill?.Value,
            properties?.GridSpan?.Val?.Value);
    }

    private static List<DocumentRevisionSnapshot> ParseRevisions(Body body)
    {
        var revisions = new List<DocumentRevisionSnapshot>();
        revisions.AddRange(body.Descendants<InsertedRun>().Select(inserted => new DocumentRevisionSnapshot(
            inserted.Id?.Value ?? string.Empty,
            DocumentRevisionKind.Insert,
            inserted.InnerText,
            inserted.Author?.Value,
            ToUtc(inserted.Date?.Value))));
        revisions.AddRange(body.Descendants<DeletedRun>().Select(deleted => new DocumentRevisionSnapshot(
            deleted.Id?.Value ?? string.Empty,
            DocumentRevisionKind.Delete,
            deleted.InnerText,
            deleted.Author?.Value,
            ToUtc(deleted.Date?.Value))));
        revisions.AddRange(body.Descendants<RunPropertiesChange>().Select(change => new DocumentRevisionSnapshot(
            change.Id?.Value ?? string.Empty,
            DocumentRevisionKind.RunPropertyChange,
            string.Empty,
            change.Author?.Value,
            ToUtc(change.Date?.Value))));
        revisions.AddRange(body.Descendants<ParagraphPropertiesChange>().Select(change => new DocumentRevisionSnapshot(
            change.Id?.Value ?? string.Empty,
            DocumentRevisionKind.ParagraphPropertyChange,
            string.Empty,
            change.Author?.Value,
            ToUtc(change.Date?.Value))));
        revisions.AddRange(body.Descendants<TablePropertiesChange>().Select(change => new DocumentRevisionSnapshot(
            change.Id?.Value ?? string.Empty,
            DocumentRevisionKind.TablePropertyChange,
            string.Empty,
            change.Author?.Value,
            ToUtc(change.Date?.Value))));
        return revisions;
    }

    private static DocumentCommentSnapshot[] ParseComments(MainDocumentPart mainPart)
    {
        var commentRoot = mainPart.WordprocessingCommentsPart?.Comments;
        if (commentRoot is null)
        {
            return [];
        }

        return commentRoot.Elements<Comment>()
            .Select(comment => new DocumentCommentSnapshot(
                comment.Id?.Value ?? string.Empty,
                comment.InnerText,
                comment.Author?.Value,
                ToUtc(comment.Date?.Value)))
            .ToArray();
    }

    private static DateTimeOffset? ToUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }
}
