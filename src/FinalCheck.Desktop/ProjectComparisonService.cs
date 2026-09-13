using FinalCheck.Core.Abstractions;
using FinalCheck.Comparison;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Management;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;
public sealed class ProjectComparisonService(IComparisonEngine engine, IServiceScopeFactory scopes) : IProjectComparisonService
{
    private Task<T> Run<T>(Func<IProjectComparisonStore, Task<T>> action, CancellationToken token) => Task.Run(async () =>
    { await using var scope = scopes.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<IProjectComparisonStore>()); }, token);
    public Task SetBaselineAsync(Guid projectId, Guid versionId, CancellationToken token = default) => Run(async s => { await s.SetBaselineAsync(projectId, versionId, token); return true; }, token);
    public Task<ProjectComparisonChoices> ChoicesAsync(Guid projectId, Guid currentVersionId, CancellationToken token = default) => Run(s => s.ChoicesAsync(projectId, currentVersionId, token), token);
    public Task<IReadOnlyList<ProjectComparisonHistory>> HistoryAsync(Guid projectId, Guid? versionId = null, int offset = 0, int limit = 20, CancellationToken token = default) => Run(s => s.HistoryAsync(projectId, versionId, offset, limit, token), token);
    public Task<ComparisonWorkflowResult> CompareAsync(ProjectComparisonSelection selection, IProgress<string>? progress = null, CancellationToken token = default) => Task.Run(async () =>
    {
        ProjectComparisonInput input;
        progress?.Report("正在加载冻结版本快照…");
        await using (var scope = scopes.CreateAsyncScope()) input = await scope.ServiceProvider.GetRequiredService<IProjectComparisonStore>().LoadInputAsync(selection, token);
        progress?.Report("正在比较已导入版本…"); var result = engine.Compare(input.Baseline, input.Current, cancellationToken: token);
        progress?.Report("正在保存独立项目比对历史…"); await using var save = scopes.CreateAsyncScope(); return await save.ServiceProvider.GetRequiredService<IProjectComparisonStore>().SaveAsync(input, result, token);
    }, token);
}
