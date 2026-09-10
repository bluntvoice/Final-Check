using System.Text.Json.Serialization;

namespace FinalCheck.Core.Documents;

public sealed record DocumentSnapshot(
    int SnapshotSchemaVersion,
    DocumentMetadataSnapshot Metadata,
    IReadOnlyList<DocumentSectionSnapshot> Sections,
    IReadOnlyList<DocumentParagraphSnapshot> Paragraphs,
    IReadOnlyList<DocumentTableSnapshot> Tables,
    IReadOnlyList<DocumentRevisionSnapshot> Revisions,
    IReadOnlyList<DocumentCommentSnapshot> Comments,
    IReadOnlyList<DocumentStyleSnapshot> Styles,
    DocumentNumberingSnapshot Numbering,
    DocumentDefaultsSnapshot Defaults,
    IReadOnlyList<DocumentHyperlinkSnapshot> Hyperlinks,
    DocumentProtectionSnapshot Protection,
    IReadOnlyList<HeaderFooterSnapshot> HeaderFooters,
    DocumentParseStatus ParseStatus,
    IReadOnlyList<DocumentParseDiagnostic> ParseDiagnostics)
{
    public const int CurrentSchemaVersion = 2;

    public static DocumentSnapshot Empty { get; } = new(
        CurrentSchemaVersion,
        DocumentMetadataSnapshot.Empty,
        [],
        [],
        [],
        [],
        [],
        [],
        DocumentNumberingSnapshot.Empty,
        DocumentDefaultsSnapshot.Empty,
        [],
        DocumentProtectionSnapshot.None,
        [],
        DocumentParseStatus.Complete,
        []);
}

public sealed record DocumentMetadataSnapshot(
    string? Title,
    string? Creator,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    string MainDocumentPart,
    int BodyElementCount,
    string Sha256)
{
    public static DocumentMetadataSnapshot Empty { get; } = new(null, null, null, null, string.Empty, 0, string.Empty);
}

public sealed record DocumentNodeIdentitySnapshot(
    string NodeId,
    string? ParentNodeId,
    DocumentNodeKind Kind,
    string StructuralPath,
    string SourcePart,
    int SourceIndex);

public sealed record DocumentParagraphSnapshot(
    DocumentNodeIdentitySnapshot Identity,
    int Index,
    string RawText,
    string DisplayText,
    bool IsEmpty,
    string? StyleId,
    IReadOnlyList<DocumentRunSnapshot> Runs,
    ParagraphFormatSnapshot DirectFormatting,
    ParagraphFormatSnapshot EffectiveFormatting,
    ParagraphNumberingReferenceSnapshot? Numbering)
{
    [JsonIgnore]
    public string NodeId => Identity.NodeId;

    [JsonIgnore]
    public string Text => DisplayText;
}

public sealed record DocumentRunSnapshot(
    DocumentNodeIdentitySnapshot Identity,
    int Index,
    string ParagraphNodeId,
    string RawText,
    string DisplayText,
    bool IsEmpty,
    IReadOnlyList<RunContentSnapshot> Content,
    CharacterFormatSnapshot DirectFormatting,
    CharacterFormatSnapshot EffectiveFormatting)
{
    [JsonIgnore]
    public string NodeId => Identity.NodeId;

    [JsonIgnore]
    public string Text => DisplayText;
}

public enum RunContentKind
{
    Text,
    DeletedText,
    Tab,
    Break,
    CarriageReturn,
    FieldCode,
    Unsupported,
}

public sealed record RunContentSnapshot(
    RunContentKind Kind,
    string RawText,
    string DisplayText,
    int Index);

public enum DocumentParseStatus
{
    Complete,
    Partial,
    Failed,
}

public enum DocumentDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record DocumentParseDiagnostic(
    DocumentDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? NodeId,
    string? SourcePart,
    bool ContentWasSkipped);
