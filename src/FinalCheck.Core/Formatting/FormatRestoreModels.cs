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

public enum FormatRestoreOperationKind { Restore, Undo, PreserveExternalChanges, Regenerate }
public enum FormatRestoreOperationStatus { Prepared, Completed, Failed, Undone }
public enum FormatRestoreResultStatus { Completed, NoChanges, WorkingCopyExternallyModified, RecoveryNeedsReview, Failed, Cancelled }

public sealed record RestoredWorkingCopy(
    Guid ContractVersionId, string OriginalPath, string OriginalSha256, string WorkingPath,
    string Sha256, DocumentSnapshot Snapshot, DateTimeOffset UpdatedAt, Guid LastOperationId);

public sealed record FormatRestoreOperation(
    int SchemaVersion, Guid OperationId, Guid ContractVersionId, FormatRestoreOperationKind Kind,
    FormatRestoreOperationStatus Status, FormatRestorePlan? Plan, FormatRestoreScope Scope,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string? BeforeSha256, string AfterSha256,
    IReadOnlyList<FormatRestoreMutation> Mutations, IReadOnlyList<FormatRestoreDiagnostic> Diagnostics,
    RestoredWorkingCopy WorkingCopy, string TemporaryPath, string BackupPath,
    Guid? UndoesOperationId = null, ComparisonResult? RebuiltComparison = null, string? PreviousMetadataSha256 = null)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record FormatRestoreResult(
    FormatRestoreResultStatus Status, RestoredWorkingCopy? WorkingCopy,
    FormatRestoreOperation? Operation, IReadOnlyList<FormatRestoreDiagnostic> Diagnostics);
