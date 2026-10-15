using FinalCheck.Core.Comparisons;

namespace FinalCheck.Core.Formatting;

public enum FormatRestoreAnalysisStatus { Ready, Unavailable, SourceChanged, WorkingCopyChanged, RecoveryRequired }

public sealed record FormatRestoreAnalysis(
    FormatRestoreAnalysisStatus Status, Guid? ContractVersionId, ComparisonWorkflowResult? Comparison,
    FormatRestorePlan? Plan, RestoredWorkingCopy? WorkingCopy, string Message)
{
    public string SourcePath { get; init; } = "";
}

/// <summary>Read-only composition of saved comparison identity and the current restoration source.</summary>
public interface IFormatRestoreWorkspaceService
{
    Task<FormatRestoreAnalysis> AnalyzeAsync(Guid comparisonRecordId, CancellationToken cancellationToken = default);
}
