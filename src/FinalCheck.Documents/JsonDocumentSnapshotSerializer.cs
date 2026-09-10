using System.Text.Json;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Documents;

namespace FinalCheck.Documents;

public sealed class JsonDocumentSnapshotSerializer : IDocumentSnapshotSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public byte[] Serialize(DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SnapshotSchemaVersion != DocumentSnapshot.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Only current schema {DocumentSnapshot.CurrentSchemaVersion} snapshots can be serialized.");
        }

        return JsonSerializer.SerializeToUtf8Bytes(snapshot, SerializerOptions);
    }

    public DocumentSnapshot Deserialize(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new InvalidDataException("The snapshot payload is empty.");
        }

        try
        {
            using var json = JsonDocument.Parse(payload.ToArray());
            if (!json.RootElement.TryGetProperty("snapshotSchemaVersion", out var versionProperty) ||
                !versionProperty.TryGetInt32(out var schemaVersion))
            {
                throw new InvalidDataException("The snapshot payload has no valid schema version.");
            }

            if (schemaVersion > DocumentSnapshot.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Snapshot schema {schemaVersion} is newer than supported schema {DocumentSnapshot.CurrentSchemaVersion}.");
            }

            if (schemaVersion == 1)
            {
                var legacy = JsonSerializer.Deserialize<LegacyDocumentSnapshot>(payload, SerializerOptions)
                    ?? throw new InvalidDataException("The schema v1 snapshot payload is invalid.");
                return MigrateV1(legacy);
            }

            if (schemaVersion != DocumentSnapshot.CurrentSchemaVersion)
            {
                throw new NotSupportedException($"Snapshot schema {schemaVersion} is not supported.");
            }

            return JsonSerializer.Deserialize<DocumentSnapshot>(payload, SerializerOptions)
                ?? throw new InvalidDataException("The snapshot payload is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The snapshot payload is not valid JSON.", exception);
        }
    }

    private static DocumentSnapshot MigrateV1(LegacyDocumentSnapshot legacy)
    {
        var paragraphs = legacy.Paragraphs.Select(MigrateParagraph).ToArray();
        var tables = legacy.Tables.Select(table => new DocumentTableSnapshot(
            Identity(table.NodeId, null, DocumentNodeKind.Table, table.Index),
            table.Index,
            table.Rows.Select(row => new DocumentTableRowSnapshot(
                Identity(row.NodeId, table.NodeId, DocumentNodeKind.Row, row.Index),
                row.Index,
                row.Cells.Select(cell => MigrateCell(cell, row.NodeId, row.Index)).ToArray(),
                null,
                null)).ToArray(),
            TableFormatSnapshot.Empty)).ToArray();
        var revisions = legacy.Revisions.Select((revision, index) => new DocumentRevisionSnapshot(
            Identity($"legacy/revision[{index}]", null, DocumentNodeKind.Run, index),
            revision.RevisionId,
            revision.Kind,
            revision.Text,
            revision.Author,
            revision.TimestampUtc,
            null,
            null,
            null,
            revision.Kind is DocumentRevisionKind.Insert or DocumentRevisionKind.Delete,
            null,
            null,
            null)).ToArray();
        var comments = legacy.Comments.Select(comment => new DocumentCommentSnapshot(
            comment.CommentId,
            comment.Text,
            comment.Author,
            null,
            comment.TimestampUtc,
            null,
            null,
            null,
            null,
            false)).ToArray();

        return new DocumentSnapshot(
            DocumentSnapshot.CurrentSchemaVersion,
            DocumentMetadataSnapshot.Empty,
            [],
            paragraphs,
            tables,
            revisions,
            comments,
            [],
            DocumentNumberingSnapshot.Empty,
            DocumentDefaultsSnapshot.Empty,
            [],
            DocumentProtectionSnapshot.None,
            [],
            DocumentParseStatus.Partial,
            [new DocumentParseDiagnostic(
                DocumentDiagnosticSeverity.Warning,
                "LegacySnapshotMigrated",
                "Schema v1 content was migrated to schema v2; structures not represented by v1 remain unavailable.",
                null,
                null,
                true)]);
    }

    private static DocumentParagraphSnapshot MigrateParagraph(LegacyParagraph paragraph)
    {
        var identity = Identity(paragraph.NodeId, null, DocumentNodeKind.Paragraph, paragraph.Index);
        var runs = paragraph.Runs.Select(run =>
        {
            var text = run.Text ?? string.Empty;
            return new DocumentRunSnapshot(
                Identity(run.NodeId, paragraph.NodeId, DocumentNodeKind.Run, run.Index),
                run.Index,
                paragraph.NodeId,
                text,
                text,
                string.IsNullOrEmpty(text),
                [new RunContentSnapshot(RunContentKind.Text, text, text, 0)],
                MigrateCharacterFormat(run.Format),
                MigrateCharacterFormat(run.Format));
        }).ToArray();
        var paragraphFormat = new ParagraphFormatSnapshot(
            paragraph.Format.Alignment,
            paragraph.Format.LeftIndent,
            null,
            paragraph.Format.FirstLineIndent,
            null,
            null,
            null,
            null,
            null);
        return new DocumentParagraphSnapshot(
            identity,
            paragraph.Index,
            paragraph.Text,
            paragraph.Text,
            string.IsNullOrEmpty(paragraph.Text),
            paragraph.Format.StyleId,
            runs,
            paragraphFormat,
            paragraphFormat,
            null);
    }

    private static DocumentTableCellSnapshot MigrateCell(LegacyCell cell, string rowNodeId, int rowIndex)
    {
        var identity = Identity(cell.NodeId, rowNodeId, DocumentNodeKind.Cell, cell.Index);
        var paragraphNodeId = cell.NodeId + "/p[0]";
        var runNodeId = paragraphNodeId + "/r[0]";
        var run = new DocumentRunSnapshot(
            Identity(runNodeId, paragraphNodeId, DocumentNodeKind.Run, 0),
            0,
            paragraphNodeId,
            cell.Text,
            cell.Text,
            string.IsNullOrEmpty(cell.Text),
            [new RunContentSnapshot(RunContentKind.Text, cell.Text, cell.Text, 0)],
            CharacterFormatSnapshot.Empty,
            CharacterFormatSnapshot.Empty);
        var paragraph = new DocumentParagraphSnapshot(
            Identity(paragraphNodeId, cell.NodeId, DocumentNodeKind.Paragraph, 0),
            0,
            cell.Text,
            cell.Text,
            string.IsNullOrEmpty(cell.Text),
            null,
            [run],
            ParagraphFormatSnapshot.Empty,
            ParagraphFormatSnapshot.Empty,
            null);
        return new DocumentTableCellSnapshot(
            identity,
            cell.Index,
            rowIndex,
            cell.Index,
            cell.Text,
            [paragraph],
            new TableCellFormatSnapshot(
                cell.Width,
                null,
                null,
                cell.ShadingFill,
                cell.GridSpan,
                null,
                TableBordersSnapshot.Empty),
            0);
    }

    private static CharacterFormatSnapshot MigrateCharacterFormat(LegacyCharacterFormat format) => new(
        new FontFamilySnapshot(format.FontFamily, format.FontFamily, null, null, null, null, null, null),
        format.FontSizeHalfPoints,
        format.Color,
        format.IsBold,
        format.IsItalic,
        format.Underline,
        null,
        null);

    private static DocumentNodeIdentitySnapshot Identity(
        string nodeId,
        string? parentNodeId,
        DocumentNodeKind kind,
        int index) => new(nodeId, parentNodeId, kind, nodeId, "/word/document.xml", index);

    private sealed record LegacyDocumentSnapshot(
        int SnapshotSchemaVersion,
        IReadOnlyList<LegacyParagraph> Paragraphs,
        IReadOnlyList<LegacyTable> Tables,
        IReadOnlyList<LegacyRevision> Revisions,
        IReadOnlyList<LegacyComment> Comments);

    private sealed record LegacyParagraph(
        string NodeId,
        int Index,
        string Text,
        IReadOnlyList<LegacyRun> Runs,
        LegacyParagraphFormat Format);

    private sealed record LegacyRun(string NodeId, int Index, string Text, LegacyCharacterFormat Format);

    private sealed record LegacyCharacterFormat(
        string? FontFamily,
        int? FontSizeHalfPoints,
        string? Color,
        bool IsBold,
        bool IsItalic,
        string? Underline);

    private sealed record LegacyParagraphFormat(
        string? StyleId,
        string? Alignment,
        string? LeftIndent,
        string? FirstLineIndent);

    private sealed record LegacyTable(string NodeId, int Index, IReadOnlyList<LegacyRow> Rows);

    private sealed record LegacyRow(string NodeId, int Index, IReadOnlyList<LegacyCell> Cells);

    private sealed record LegacyCell(
        string NodeId,
        int Index,
        string Text,
        string? Width,
        string? ShadingFill,
        int? GridSpan);

    private sealed record LegacyRevision(
        string RevisionId,
        DocumentRevisionKind Kind,
        string Text,
        string? Author,
        DateTimeOffset? TimestampUtc);

    private sealed record LegacyComment(string CommentId, string Text, string? Author, DateTimeOffset? TimestampUtc);
}
