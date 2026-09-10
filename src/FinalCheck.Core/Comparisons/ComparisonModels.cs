using FinalCheck.Core.Documents;

namespace FinalCheck.Core.Comparisons;

public enum ComparisonChangeKind
{
    TextInsert,
    TextDelete,
    TextReplace,
    ParagraphInsert,
    ParagraphDelete,
    ParagraphMove,
    ParagraphMoveAndModify,
    CharacterFormatChange,
    ParagraphFormatChange,
    TableChange,
    TableCellChange,
    Comment,
    NativeRevision,
}

public enum DifferenceOperation
{
    Insert,
    Delete,
    Replace,
}

public enum ComparisonMappingType
{
    Structural,
    ExactText,
    HeadingOrNumbering,
    SimilarText,
    Contextual,
}

public enum ComparisonConfidenceLevel
{
    Exact,
    High,
    Medium,
    Low,
}

public enum ComparisonEvidenceKind
{
    StructuralPath,
    ExactText,
    NormalizedText,
    Position,
    Context,
    Style,
    Numbering,
    Heading,
    TextSimilarity,
    SnapshotDifference,
    NativeRevision,
    Comment,
}

public enum ComparisonDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public enum FormatDifferenceScope
{
    Character,
    Paragraph,
    Table,
    TableCell,
}

public enum ComparisonStage
{
    Preparing,
    MatchingStructure,
    ComparingText,
    DetectingMoves,
    ComparingFormatting,
    ProcessingRevisionsAndComments,
    GroupingChanges,
    Completed,
}

public sealed record ComparisonMetadata(
    string BaselineSnapshotId,
    string CurrentSnapshotId,
    int BaselineSnapshotSchemaVersion,
    int CurrentSnapshotSchemaVersion,
    string AlgorithmVersion);

public sealed record ComparisonMatchEvidence(
    ComparisonEvidenceKind Kind,
    double Score,
    string? Detail);

public sealed record ComparisonNodeMapping(
    DocumentNodeReference BaselineNode,
    DocumentNodeReference CurrentNode,
    ComparisonMappingType MappingType,
    ComparisonConfidenceLevel Confidence,
    double Score,
    IReadOnlyList<ComparisonMatchEvidence> Evidence,
    bool IsMoved,
    bool IsModified);

public sealed record DifferenceSpan(
    DifferenceOperation Operation,
    int BaselineStart,
    int BaselineLength,
    int CurrentStart,
    int CurrentLength,
    string OldText,
    string NewText);

public sealed record FormatPropertyDifference(
    string Property,
    string? BaselineValue,
    string? CurrentValue);

public sealed record ComparisonFormatDifference(
    FormatDifferenceScope Scope,
    string? BaselineNodeId,
    string? CurrentNodeId,
    IReadOnlyList<FormatPropertyDifference> Properties);

public sealed record ComparisonChangeItem(
    string ChangeId,
    ComparisonChangeKind Kind,
    string? BaselineNodeId,
    string? CurrentNodeId,
    string? BaselineLocation,
    string? CurrentLocation,
    string BaselineText,
    string CurrentText,
    IReadOnlyList<DifferenceSpan> DifferenceSpans,
    ComparisonFormatDifference? FormatDifference,
    IReadOnlyList<string> RevisionIds,
    IReadOnlyList<string> CommentIds,
    IReadOnlyList<ComparisonEvidenceKind> SourceEvidence,
    ComparisonConfidenceLevel? MatchConfidence,
    IReadOnlyList<string> DiagnosticCodes);

public sealed record ComparisonChangeGroup(
    string GroupId,
    ComparisonChangeKind Kind,
    string BaselineText,
    string CurrentText,
    IReadOnlyList<string> ChangeIds);

public sealed record ComparisonDiagnostic(
    ComparisonDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? BaselineNodeId,
    string? CurrentNodeId);

public sealed record ComparisonStatistics(
    int TotalChanges,
    int TextChanges,
    int FormatChanges,
    int ParagraphsAdded,
    int ParagraphsDeleted,
    int ParagraphsMoved,
    int Comments,
    int GroupCount,
    int LowConfidenceMappings)
{
    public static ComparisonStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record ComparisonProgress(ComparisonStage Stage, int ProcessedItems, int? TotalItems);

public sealed record ComparisonResult(
    int ComparisonSchemaVersion,
    ComparisonMetadata Metadata,
    IReadOnlyList<ComparisonNodeMapping> NodeMappings,
    IReadOnlyList<ComparisonChangeItem> Changes,
    IReadOnlyList<ComparisonChangeGroup> Groups,
    IReadOnlyList<ComparisonDiagnostic> Diagnostics,
    ComparisonStatistics Statistics)
{
    public const int CurrentSchemaVersion = 1;
}
