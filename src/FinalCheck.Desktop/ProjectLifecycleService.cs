using FinalCheck.Core.Management;
using FinalCheck.Data;
using FinalCheck.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;
public sealed class ProjectLifecycleService(IServiceScopeFactory scopes) : IProjectLifecycleService
{
    private Task<T> Run<T>(Func<IProjectLifecycleStore, Task<T>> action, CancellationToken token) => Task.Run(async () =>
    { await using var scope = scopes.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<IProjectLifecycleStore>()); }, token);
    public Task<ProjectSummary> SummaryAsync(Guid projectId, CancellationToken token = default) => Run(s => s.SummaryAsync(projectId, token), token);
    public Task<VersionRestoreState> RestoreStateAsync(Guid versionId, CancellationToken token = default) => Run(s => s.RestoreStateAsync(versionId, token), token);
    public Task SetStatusAsync(Guid projectId, ProjectStatus status, CancellationToken token = default) => Run(async s => { await s.SetStatusAsync(projectId, status, token); return true; }, token);
    public Task<ProjectDeletionRecord> DeleteAsync(Guid projectId, bool confirmed, CancellationToken token = default) => Task.Run(async () =>
    {
        if (!confirmed) throw new ArgumentException("永久删除必须二次确认。");
        await using var scope = scopes.CreateAsyncScope(); var store = scope.ServiceProvider.GetRequiredService<IProjectLifecycleStore>(); var paths = scope.ServiceProvider.GetRequiredService<FinalCheckDbContext>().ManagedPaths;
        var versions = await store.VersionIdsAsync(projectId, token); using var locks = ManagedProjectFileCleanup.LockVersions(paths, versions);
        var record = await store.DeleteAsync(projectId, confirmed, versions, paths, token);
        // Database commit is final. Cleanup/journal updates cannot report successful deletion as cancellation.
        record = await ManagedProjectFileCleanup.RunAsync(record, paths);
        try { await store.SaveCleanupAsync(record, CancellationToken.None); }
        catch (Exception e) when (e is IOException or Microsoft.EntityFrameworkCore.DbUpdateException) { return record with { Status = "PendingCleanup", Diagnostic = "项目记录已永久删除；清理审计更新失败，启动恢复将重新核对。" }; }
        return record;
    }, token);
    public static async Task RecoverAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope(); var store = scope.ServiceProvider.GetRequiredService<IProjectLifecycleStore>(); var paths = scope.ServiceProvider.GetRequiredService<FinalCheckDbContext>().ManagedPaths;
        foreach (var record in await store.PendingCleanupAsync())
        {
            try { using var locks = ManagedProjectFileCleanup.LockVersions(paths, record.VersionIds); await store.SaveCleanupAsync(await ManagedProjectFileCleanup.RunAsync(record, paths)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Durable journal remains; startup never discards inaccessible data. */ }
        }
    }
}
