using FinalCheck.Core.Management;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;

public sealed class ProjectService(IServiceScopeFactory scopes) : IProjectService
{
    private Task<T> Run<T>(Func<IProjectStore, Task<T>> action, CancellationToken token) => Task.Run(async () =>
    { await using var scope = scopes.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<IProjectStore>()); }, token);
    public Task<IReadOnlyList<ProjectListItem>> ListAsync(ProjectQuery query, CancellationToken token = default) => Run(s => s.ListAsync(query, token), token);
    public Task<ContractProject> GetAsync(Guid id, CancellationToken token = default) => Run(s => s.GetAsync(id, token), token);
    public Task<ContractProject> SaveAsync(Guid? id, ProjectEdit edit, CancellationToken token = default) => Run(s => s.SaveAsync(id, edit, token), token);
    public Task<IReadOnlyList<ProjectFolder>> FoldersAsync(CancellationToken token = default) => Run(s => s.FoldersAsync(token), token);
}
