using FinalCheck.Comparison;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison.Tests;

internal static class ComparisonFixtureFactory
{
    public static DocumentSnapshot FromText(params string[] paragraphs) => Create(
        paragraphs.Select(text => new ParagraphSpec(text)).ToArray());

    public static DocumentSnapshot Create(params ParagraphSpec[] paragraphs) => DocumentSnapshot.Empty with
    {
        Paragraphs = paragraphs.Select(CreateParagraph).ToArray(),
    };

    public static DocumentSnapshot WithTables(params TableSpec[] tables) => DocumentSnapshot.Empty with
    {
        Tables = tables.Select(CreateTable).ToArray(),
    };

    public static BasicComparisonEngine Engine()
    {
        var textDiff = new TokenTextDiffService(new MixedLanguageTextTokenizer());
        var formatDiff = new EffectiveFormatDiffService();
        return new BasicComparisonEngine(
            new MultiSignalParagraphMatcher(),
            textDiff,
            new ParagraphMoveDetector(),
            formatDiff,
            new TableComparisonService(textDiff, formatDiff),
            new SnapshotAnnotationIntegrationService(),
            new RuleBasedChangeGroupingService());
    }

    private static DocumentParagraphSnapshot CreateParagraph(ParagraphSpec spec, int index)
    {
        var paragraphId = $"body/p[{index}]";
        var runTexts = spec.Runs is { Count: > 0 } ? spec.Runs : [spec.Text];
        var runs = runTexts.Select((text, runIndex) => new DocumentRunSnapshot(
            new DocumentNodeIdentitySnapshot(
                $"{paragraphId}/r[{runIndex}]",
                paragraphId,
                DocumentNodeKind.Run,
                $"{paragraphId}/r[{runIndex}]",
                "/word/document.xml",
                runIndex),
            runIndex,
            paragraphId,
            text,
            text,
            string.IsNullOrEmpty(text),
            [new RunContentSnapshot(RunContentKind.Text, text, text, 0)],
            spec.CharacterFormatting ?? CharacterFormatSnapshot.Empty,
            spec.CharacterFormatting ?? CharacterFormatSnapshot.Empty)).ToArray();
        return new DocumentParagraphSnapshot(
            new DocumentNodeIdentitySnapshot(
                paragraphId,
                null,
                DocumentNodeKind.Paragraph,
                paragraphId,
                "/word/document.xml",
                index),
            index,
            spec.Text,
            spec.Text,
            string.IsNullOrEmpty(spec.Text),
            spec.StyleId,
            runs,
            spec.ParagraphFormatting ?? ParagraphFormatSnapshot.Empty,
            spec.ParagraphFormatting ?? ParagraphFormatSnapshot.Empty,
            spec.Numbering);
    }

    private static DocumentTableSnapshot CreateTable(TableSpec spec, int tableIndex)
    {
        var tableId = $"body/tbl[{tableIndex}]";
        var rows = spec.Rows.Select((row, rowIndex) => new DocumentTableRowSnapshot(
            new DocumentNodeIdentitySnapshot(
                $"{tableId}/tr[{rowIndex}]",
                tableId,
                DocumentNodeKind.Row,
                $"{tableId}/tr[{rowIndex}]",
                "/word/document.xml",
                rowIndex),
            rowIndex,
            row.Cells.Select((cell, cellIndex) => CreateCell(tableId, rowIndex, cellIndex, cell)).ToArray(),
            null,
            null)).ToArray();
        return new DocumentTableSnapshot(
            new DocumentNodeIdentitySnapshot(
                tableId,
                null,
                DocumentNodeKind.Table,
                tableId,
                "/word/document.xml",
                tableIndex),
            tableIndex,
            rows,
            spec.Formatting ?? TableFormatSnapshot.Empty);
    }

    private static DocumentTableCellSnapshot CreateCell(
        string tableId,
        int rowIndex,
        int columnIndex,
        CellSpec spec)
    {
        var rowId = $"{tableId}/tr[{rowIndex}]";
        var cellId = $"{rowId}/tc[{columnIndex}]";
        return new DocumentTableCellSnapshot(
            new DocumentNodeIdentitySnapshot(
                cellId,
                rowId,
                DocumentNodeKind.Cell,
                cellId,
                "/word/document.xml",
                columnIndex),
            columnIndex,
            rowIndex,
            columnIndex,
            spec.Text,
            [],
            spec.Formatting ?? TableCellFormatSnapshot.Empty,
            0);
    }

    internal sealed record ParagraphSpec(
        string Text,
        string? StyleId = null,
        ParagraphNumberingReferenceSnapshot? Numbering = null,
        ParagraphFormatSnapshot? ParagraphFormatting = null,
        CharacterFormatSnapshot? CharacterFormatting = null,
        IReadOnlyList<string>? Runs = null);

    internal sealed record TableSpec(
        IReadOnlyList<RowSpec> Rows,
        TableFormatSnapshot? Formatting = null);

    internal sealed record RowSpec(IReadOnlyList<CellSpec> Cells);

    internal sealed record CellSpec(string Text, TableCellFormatSnapshot? Formatting = null);
}
