using FinalCheck.Core.Storage;

namespace FinalCheck.Core.Management;
public sealed record ProjectSummary(Guid ProjectId, ProjectStatus Status, string CurrentBaseline, string LatestVersion, ProjectComparisonHistory? LatestComparison, int PendingRestoreCount);
public sealed record VersionRestoreState(string Message, string? WorkingPath, string? Sha256, int PendingCount);
public sealed record ManagedProjectCleanupFile(string RelativePath, string Sha256);
public sealed record ProjectDeletionRecord(int SchemaVersion, Guid OperationId, Guid ProjectId, IReadOnlyList<Guid> VersionIds,
    IReadOnlyList<ManagedProjectCleanupFile> Files, IReadOnlyList<string> OriginalPaths, DateTimeOffset CreatedAt, string Status, string Diagnostic);
public interface IProjectLifecycleStore
{
    Task<ProjectSummary> SummaryAsync(Guid projectId, CancellationToken token = default);
    Task<VersionRestoreState> RestoreStateAsync(Guid versionId, CancellationToken token = default);
    Task SetStatusAsync(Guid projectId, ProjectStatus status, CancellationToken token = default);
    Task<IReadOnlyList<Guid>> VersionIdsAsync(Guid projectId, CancellationToken token = default);
    Task<ProjectDeletionRecord> DeleteAsync(Guid projectId, bool confirmed, IReadOnlyList<Guid> lockedVersions, IDataRootProvider paths, CancellationToken token = default);
    Task<IReadOnlyList<ProjectDeletionRecord>> PendingCleanupAsync(CancellationToken token = default);
    Task SaveCleanupAsync(ProjectDeletionRecord record, CancellationToken token = default);
}
public interface IProjectLifecycleService
{
    Task<ProjectSummary> SummaryAsync(Guid projectId, CancellationToken token = default);
    Task<VersionRestoreState> RestoreStateAsync(Guid versionId, CancellationToken token = default);
    Task SetStatusAsync(Guid projectId, ProjectStatus status, CancellationToken token = default);
    Task<ProjectDeletionRecord> DeleteAsync(Guid projectId, bool confirmed, CancellationToken token = default);
}
