using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Management;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;

public sealed class ContractVersionService(IComparisonFileInspector files, IDocumentParser parser, IServiceScopeFactory scopes) : IContractVersionService
{
    private Task<T> Run<T>(Func<IContractVersionStore, Task<T>> action, CancellationToken token) => Task.Run(async () =>
    { await using var scope = scopes.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<IContractVersionStore>()); }, token);
    public Task<IReadOnlyList<ContractVersion>> ListAsync(Guid projectId, bool chronological = false, int offset = 0, int limit = 20, CancellationToken token = default) => Run(s => s.ListAsync(projectId, chronological, offset, limit, token), token);
    public Task<IReadOnlyList<NegotiationRound>> RoundsAsync(Guid projectId, CancellationToken token = default) => Run(s => s.RoundsAsync(projectId, token), token);
    public Task<IReadOnlyList<ContractVersion>> SameContentAsync(Guid projectId, string sha256, CancellationToken token = default) => Run(s => s.SameContentAsync(projectId, sha256, token), token);
    public Task<ComparisonFile> InspectAsync(string path, CancellationToken token = default) => Task.Run(() => files.InspectAsync(path, token), token);
    public Task<ContractVersion> GetAsync(Guid id, CancellationToken token = default) => Run(s => s.GetAsync(id, token), token);
    public Task<DocumentSnapshot> LoadSnapshotAsync(Guid id, CancellationToken token = default) => Run(s => s.LoadSnapshotAsync(id, token), token);
    public Task<IReadOnlyList<ContractVersion>> ImportAsync(Guid projectId, IReadOnlyList<VersionImport> versions, CancellationToken token = default) => Task.Run(async () =>
    {
        if (versions.Count is < 1 or > 100) throw new ArgumentException("一次导入 1–100 份版本。"); var prepared = new List<PreparedVersion>();
        foreach (var request in versions)
        {
            if (!Enum.IsDefined(request.Role)) throw new ArgumentException("必须明确选择我方或对方。");
            var source = await files.InspectAsync(request.Path, token);
            if (request.ExpectedSha256 is { } expected && expected != source.Sha256) throw new IOException("文件在加入队列后已变化，请移除后重新加入并确认。");
            await using var stream = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var snapshot = await parser.ParseAsync(stream, cancellationToken: token); var after = await files.InspectAsync(request.Path, token);
            if (source.Sha256 != snapshot.Metadata.Sha256 || source.Sha256 != after.Sha256) throw new IOException("文件在导入期间变化，请重试。");
            prepared.Add(new(request, source, snapshot));
        }
        await using var scope = scopes.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<IContractVersionStore>().ImportAsync(projectId, prepared, token);
    }, token);
    public async Task RelinkAsync(Guid id, string path, CancellationToken token = default)
    {
        var source = await InspectAsync(path, token);
        await Task.Run(async () => { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<IContractVersionStore>().RelinkAsync(id, source, token); }, token);
    }
    public Task<IReadOnlyList<ComparisonFile>> FindRelinkCandidatesAsync(ContractVersion version, CancellationToken token = default) => SourceRelinkScanner.FindAsync(files, version.Source, token);
}
