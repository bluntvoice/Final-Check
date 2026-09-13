using FinalCheck.Core.Documents;

namespace FinalCheck.Core.Comparisons;

/// <summary>External input identity only. The original DOCX is never imported into DataRoot.</summary>
public sealed record ComparisonFile(string Path, string Name, long Size, DateTimeOffset ModifiedAt, string Sha256);

public interface IComparisonFileInspector
{
    Task<ComparisonFile> InspectAsync(string path, CancellationToken cancellationToken = default);
}

public enum ComparisonReviewState { Unresolved, Confirmed, Ignored }
public sealed record ComparisonRecord(int SchemaVersion, Guid RecordId, ComparisonFile BaselineFile,
    ComparisonFile CurrentFile, Guid BaselineSnapshotId, Guid CurrentSnapshotId, Guid ResultId,
    DateTimeOffset CreatedAt, IReadOnlyDictionary<string, ComparisonReviewState> ReviewStates)
{
    public const int CurrentSchemaVersion = 1;
}
public sealed record ComparisonWorkflowResult(ComparisonRecord Record, DocumentSnapshot Baseline,
    DocumentSnapshot Current, ComparisonResult Result)
{
    public bool IsPartial => Baseline.ParseStatus != DocumentParseStatus.Complete || Current.ParseStatus != DocumentParseStatus.Complete;
}
public sealed record ComparisonInputValidation(ComparisonFile Baseline, ComparisonFile Current, bool SamePath, bool SameHash, bool Changed);
public interface IComparisonRecordStore
{
    Task<ComparisonWorkflowResult> SaveAsync(ComparisonFile baselineFile, ComparisonFile currentFile,
        DocumentSnapshot baseline, DocumentSnapshot current, ComparisonResult result, CancellationToken cancellationToken = default);
    Task<ComparisonWorkflowResult?> LoadAsync(Guid recordId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ComparisonRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<ComparisonRecord> UpdateReviewAsync(Guid recordId, IReadOnlyList<string> changeIds, ComparisonReviewState state, CancellationToken cancellationToken = default);
}
public interface IComparisonWorkflowService
{
    Task<ComparisonInputValidation> ValidateAsync(ComparisonFile baseline, ComparisonFile current, CancellationToken cancellationToken = default);
    Task<ComparisonWorkflowResult> ExecuteAsync(ComparisonInputValidation input, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ComparisonRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<ComparisonWorkflowResult?> LoadAsync(Guid recordId, CancellationToken cancellationToken = default);
    Task<ComparisonRecord> UpdateReviewAsync(Guid recordId, IReadOnlyList<string> changeIds, ComparisonReviewState state, CancellationToken cancellationToken = default);
}
