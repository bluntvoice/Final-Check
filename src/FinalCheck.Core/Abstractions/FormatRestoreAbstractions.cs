using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Core.Abstractions;

public interface IFormatRestorePlanner
{
    FormatRestorePlan Generate(
        DocumentSnapshot baseline, DocumentSnapshot current, ComparisonResult comparison,
        FormatRestorePolicy? policy = null, DateTimeOffset? createdAt = null,
        IProgress<FormatRestoreProgress>? progress = null, CancellationToken cancellationToken = default);
}

public interface IFormatRestoreRenderer
{
    ValueTask<FormatRestoreRenderResult> RenderAsync(Stream source, FormatRestorePlan plan,
        FormatRestoreScope? scope = null, IProgress<FormatRestoreProgress>? progress = null,
        CancellationToken cancellationToken = default);

    ValueTask<FormatRestoreRenderResult> RevertAsync(Stream source, string expectedSha256,
        IReadOnlyList<FormatRestoreMutation> mutations, CancellationToken cancellationToken = default);
}

public interface IFormatRestoreStore
{
    Task<RestoredWorkingCopy?> LoadWorkingCopyAsync(Guid contractVersionId, CancellationToken cancellationToken = default);
    Task<FormatRestoreOperation?> LoadLastOperationAsync(Guid contractVersionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FormatRestoreOperation>> LoadPendingAsync(Guid contractVersionId, CancellationToken cancellationToken = default);
    Task SavePreparedAsync(FormatRestoreOperation operation, CancellationToken cancellationToken = default);
    Task CompleteAsync(FormatRestoreOperation operation, CancellationToken cancellationToken = default);
    Task FailAsync(FormatRestoreOperation operation, CancellationToken cancellationToken = default);
}

public interface IWorkingCopyComparisonService
{
    ComparisonResult Compare(DocumentSnapshot baseline, DocumentSnapshot current, CancellationToken cancellationToken = default);
}

public interface IFormatRestoreWorkingCopyService
{
    Task<FormatRestoreResult> ExecuteAsync(Guid contractVersionId, string originalPath, FormatRestorePlan plan,
        FormatRestoreScope? scope = null, IProgress<FormatRestoreProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<FormatRestoreResult> UndoLastAsync(Guid contractVersionId, CancellationToken cancellationToken = default);
    Task<FormatRestoreResult> PreserveExternalChangesAsync(Guid contractVersionId, DocumentSnapshot baseline, CancellationToken cancellationToken = default);
    Task<FormatRestoreResult> RegenerateAsync(Guid contractVersionId, CancellationToken cancellationToken = default);
    Task<FormatRestoreResult> RecoverAsync(Guid contractVersionId, CancellationToken cancellationToken = default);
}
