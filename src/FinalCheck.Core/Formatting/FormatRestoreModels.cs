using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Core.Formatting;

public enum FormatRestoreCategory { Character, Paragraph, Table, Cell }
public enum FormatRestoreEligibility { Eligible, NeedsReview, Unsupported }
public enum FormatRestorePlanStatus { Ready, NeedsReview, Empty }
public enum FormatRestoreStage
{
    Preparing, ResolvingMappings, ResolvingTargetFormatting, ValidatingPlan,
    PreparingWorkingCopy, ApplyingCharacterFormatting, ApplyingParagraphFormatting,
    ApplyingTableFormatting, Saving, Reparsing, Validating, Completed,
}
public enum FormatRestoreScopeKind { All, Category, SelectedItems }

public sealed record FormatRestoreDiagnostic(string Code, string? NodeId, string Message);
public sealed record FormatRestoreProgress(FormatRestoreStage Stage, int ProcessedItems, int? TotalItems);
public sealed record FormatRestorePolicy(double MinimumScore = 0.8, string Version = "format-restore-v0.1");
public sealed record FormatRestoreScope(
    FormatRestoreScopeKind Kind = FormatRestoreScopeKind.All,
    FormatRestoreCategory? Category = null,
    IReadOnlyList<string>? SelectedItemIds = null);

public sealed record RestoreFormatting(
    CharacterFormatSnapshot? Character = null,
    ParagraphFormatSnapshot? Paragraph = null,
    TableFormatSnapshot? Table = null,
    TableCellFormatSnapshot? Cell = null,
    string? ParagraphStyleId = null,
    uint? RowHeightTwips = null,
    string? RowHeightRule = null);

public sealed record FormatRestoreItem(
    string RestoreItemId,
    string? BaselineNodeId,
    string CurrentNodeId,
    DocumentNodeKind NodeType,
    DocumentNodeIdentitySnapshot Location,
    RestoreFormatting CurrentFormatting,
    RestoreFormatting TargetFormatting,
    IReadOnlyList<string> Difference,
    FormatRestoreCategory Category,
    FormatRestoreEligibility Eligibility,
    string? FallbackSource,
    double Confidence,
    ComparisonNodeMapping? Mapping,
    IReadOnlyList<FormatRestoreDiagnostic> Diagnostics);

public sealed record FormatRestorePlan(
    int SchemaVersion,
    string PlanId,
    string SourceDocumentIdentity,
    string BaselineSnapshotIdentity,
    string CurrentSnapshotIdentity,
    string SourceSha256,
    string MappingReference,
    DateTimeOffset CreatedAt,
    FormatRestorePolicy Policy,
    IReadOnlyList<FormatRestoreItem> RestoreItems,
    IReadOnlyList<FormatRestoreDiagnostic> Diagnostics,
    FormatRestorePlanStatus Status)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record FormatRestoreMutation(
    string NodeId, string PropertyKind, string? BeforePropertiesXml, string? AfterPropertiesXml);

public sealed record FormatRestoreRenderResult(
    byte[] DocumentBytes, DocumentSnapshot Snapshot,
    IReadOnlyList<FormatRestoreMutation> Mutations,
    IReadOnlyList<FormatRestoreDiagnostic> Diagnostics);
