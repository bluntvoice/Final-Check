namespace FinalCheck.Core.Documents;

public enum DocumentRevisionKind
{
    Insert,
    Delete,
    RunPropertyChange,
    ParagraphPropertyChange,
    TablePropertyChange,
}

public sealed record DocumentRevisionSnapshot(
    DocumentNodeIdentitySnapshot Identity,
    string RevisionId,
    DocumentRevisionKind Kind,
    string Text,
    string? Author,
    DateTimeOffset? TimestampUtc,
    string? AffectedNodeId,
    string? ParagraphNodeId,
    string? RunNodeId,
    bool IsSupported,
    CharacterFormatSnapshot? PreviousCharacterFormatting,
    ParagraphFormatSnapshot? PreviousParagraphFormatting,
    TableFormatSnapshot? PreviousTableFormatting);

public sealed record DocumentCommentSnapshot(
    string CommentId,
    string Text,
    string? Author,
    string? Initials,
    DateTimeOffset? TimestampUtc,
    string? AnchorStartNodeId,
    string? AnchorEndNodeId,
    string? ParagraphNodeId,
    string? RunNodeId,
    bool IsAnchored);
