namespace FinalCheck.Core.Documents;

public sealed record DocumentSnapshot(
    int SnapshotSchemaVersion,
    IReadOnlyList<DocumentParagraphSnapshot> Paragraphs,
    IReadOnlyList<DocumentTableSnapshot> Tables,
    IReadOnlyList<DocumentRevisionSnapshot> Revisions,
    IReadOnlyList<DocumentCommentSnapshot> Comments)
{
    public const int CurrentSchemaVersion = 1;

    public static DocumentSnapshot Empty { get; } = new(
        CurrentSchemaVersion,
        [],
        [],
        [],
        []);
}

public sealed record DocumentParagraphSnapshot(
    string NodeId,
    int Index,
    string Text,
    IReadOnlyList<DocumentRunSnapshot> Runs,
    ParagraphFormatSnapshot Format);

public sealed record DocumentRunSnapshot(
    string NodeId,
    int Index,
    string Text,
    CharacterFormatSnapshot Format);

public sealed record CharacterFormatSnapshot(
    string? FontFamily,
    int? FontSizeHalfPoints,
    string? Color,
    bool IsBold,
    bool IsItalic,
    string? Underline);

public sealed record ParagraphFormatSnapshot(
    string? StyleId,
    string? Alignment,
    string? LeftIndent,
    string? FirstLineIndent);

public sealed record DocumentTableSnapshot(
    string NodeId,
    int Index,
    IReadOnlyList<DocumentTableRowSnapshot> Rows);

public sealed record DocumentTableRowSnapshot(
    string NodeId,
    int Index,
    IReadOnlyList<DocumentTableCellSnapshot> Cells);

public sealed record DocumentTableCellSnapshot(
    string NodeId,
    int Index,
    string Text,
    string? Width,
    string? ShadingFill,
    int? GridSpan);

public enum DocumentRevisionKind
{
    Insert,
    Delete,
    RunPropertyChange,
    ParagraphPropertyChange,
    TablePropertyChange,
}

public sealed record DocumentRevisionSnapshot(
    string RevisionId,
    DocumentRevisionKind Kind,
    string Text,
    string? Author,
    DateTimeOffset? TimestampUtc);

public sealed record DocumentCommentSnapshot(
    string CommentId,
    string Text,
    string? Author,
    DateTimeOffset? TimestampUtc);
