using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Core.Management;
public sealed record ProjectBaselineOption(ProjectBaselineType Type, Guid VersionId, string Name, bool IsCurrent);
public sealed record ProjectComparisonChoices(Guid ProjectId, Guid CurrentVersionId, IReadOnlyList<ProjectBaselineOption> Options,
    ProjectBaselineType? LastType, string Recommendation);
public sealed record ProjectComparisonSelection(Guid ProjectId, Guid CurrentVersionId, ProjectBaselineType BaselineType, Guid BaselineVersionId);
public sealed record ProjectComparisonInput(ProjectComparisonSelection Selection, ComparisonFile BaselineFile, ComparisonFile CurrentFile, DocumentSnapshot Baseline, DocumentSnapshot Current, string BaselineName);
public sealed record ProjectComparisonHistory(Guid RecordId, Guid ProjectId, Guid CurrentVersionId, ProjectBaselineType BaselineType, Guid BaselineVersionId,
    string CurrentName, string BaselineName, DateTimeOffset CreatedAt, int Total, int Unresolved, int Reviewed, int Ignored);
public interface IProjectComparisonStore
{
    Task SetBaselineAsync(Guid projectId, Guid versionId, CancellationToken token = default);
    Task<ProjectComparisonChoices> ChoicesAsync(Guid projectId, Guid currentVersionId, CancellationToken token = default);
    Task<ProjectComparisonInput> LoadInputAsync(ProjectComparisonSelection selection, CancellationToken token = default);
    Task<ComparisonWorkflowResult> SaveAsync(ProjectComparisonInput input, ComparisonResult result, CancellationToken token = default);
    Task<IReadOnlyList<ProjectComparisonHistory>> HistoryAsync(Guid projectId, Guid? versionId = null, int offset = 0, int limit = 20, CancellationToken token = default);
}
public interface IProjectComparisonService
{
    Task SetBaselineAsync(Guid projectId, Guid versionId, CancellationToken token = default);
    Task<ProjectComparisonChoices> ChoicesAsync(Guid projectId, Guid currentVersionId, CancellationToken token = default);
    Task<ComparisonWorkflowResult> CompareAsync(ProjectComparisonSelection selection, IProgress<string>? progress = null, CancellationToken token = default);
    Task<IReadOnlyList<ProjectComparisonHistory>> HistoryAsync(Guid projectId, Guid? versionId = null, int offset = 0, int limit = 20, CancellationToken token = default);
}
