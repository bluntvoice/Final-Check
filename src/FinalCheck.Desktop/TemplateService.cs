using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Management;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;

public sealed class TemplateService(IComparisonFileInspector files, IDocumentParser parser, IServiceScopeFactory scopes) : ITemplateService
{
    private Task<T> Run<T>(Func<ITemplateStore, Task<T>> action, CancellationToken token) => Task.Run(async () =>
    { await using var scope = scopes.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<ITemplateStore>()); }, token);
    private Task Run(Func<ITemplateStore, Task> action, CancellationToken token) => Task.Run(async () =>
    { await using var scope = scopes.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<ITemplateStore>()); }, token);
    public Task<IReadOnlyList<Template>> ListAsync(int offset = 0, int limit = 100, CancellationToken token = default) => Run(s => s.ListAsync(offset, limit, token), token);
    public Task<TemplateDetails> GetAsync(Guid id, CancellationToken token = default) => Run(s => s.GetAsync(id, token), token);
    public Task<Core.Documents.DocumentSnapshot> LoadSnapshotAsync(Guid versionId, CancellationToken token = default) => Run(s => s.LoadSnapshotAsync(versionId, token), token);
    public Task<TemplateDetails> ImportAsync(Guid? templateId, string name, string contractType, string version, string path, CancellationToken token = default) => Task.Run(async () =>
    {
        var source = await files.InspectAsync(path, token);
        await using var stream = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var snapshot = await parser.ParseAsync(stream, cancellationToken: token);
        var after = await files.InspectAsync(path, token);
        if (source.Sha256 != snapshot.Metadata.Sha256 || source.Sha256 != after.Sha256) throw new IOException("文件在导入期间变化，请重试。");
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITemplateStore>().AddVersionAsync(templateId, name, contractType, version, source, snapshot, token);
    }, token);
    public Task UpdateAsync(Guid id, string name, string contractType, string notes, bool enabled, CancellationToken token = default) => Run(s => s.UpdateAsync(id, name, contractType, notes, enabled, token), token);
    public Task SetCurrentAsync(Guid id, Guid versionId, CancellationToken token = default) => Run(s => s.SetCurrentAsync(id, versionId, token), token);
    public async Task RelinkAsync(Guid versionId, string path, CancellationToken token = default)
    { var source = await files.InspectAsync(path, token); await Run(s => s.RelinkAsync(versionId, source, token), token); }
    public Task<TemplateReferences> ReferencesAsync(Guid id, CancellationToken token = default) => Run(s => s.ReferencesAsync(id, token), token);
    public Task DeleteAsync(Guid id, bool confirmed, CancellationToken token = default) => Run(s => s.DeleteAsync(id, confirmed, token), token);
    public Task<IReadOnlyList<ComparisonFile>> FindRelinkCandidatesAsync(TemplateVersion version, CancellationToken token = default) => SourceRelinkScanner.FindAsync(files, version.Source, token);
}
