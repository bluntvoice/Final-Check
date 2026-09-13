using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Core.Management;

public sealed record Template(Guid TemplateId, string Name, string ContractType, bool IsEnabled,
    bool IsDeleted, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Notes);
public sealed record TemplateVersion(Guid TemplateVersionId, Guid TemplateId, string Version,
    ComparisonFile Source, Guid SnapshotId, bool IsCurrent, DateTimeOffset CreatedAt,
    DocumentParseStatus ParseStatus);
public sealed record TemplateDetails(Template Template, IReadOnlyList<TemplateVersion> Versions)
{
    public TemplateVersion? Current => Versions.SingleOrDefault(v => v.IsCurrent);
}
public sealed record TemplateReferences(IReadOnlyList<string> Projects, int Comparisons)
{
    public bool HasReferences => Projects.Count > 0 || Comparisons > 0;
}
public interface ITemplateStore
{
    Task<IReadOnlyList<Template>> ListAsync(int offset = 0, int limit = 100, CancellationToken token = default);
    Task<TemplateDetails> GetAsync(Guid id, CancellationToken token = default);
    Task<DocumentSnapshot> LoadSnapshotAsync(Guid versionId, CancellationToken token = default);
    Task<TemplateDetails> AddVersionAsync(Guid? templateId, string name, string contractType, string version,
        ComparisonFile source, DocumentSnapshot snapshot, CancellationToken token = default);
    Task UpdateAsync(Guid id, string name, string contractType, string notes, bool enabled, CancellationToken token = default);
    Task SetCurrentAsync(Guid id, Guid versionId, CancellationToken token = default);
    Task RelinkAsync(Guid versionId, ComparisonFile source, CancellationToken token = default);
    Task<TemplateReferences> ReferencesAsync(Guid id, CancellationToken token = default);
    Task DeleteAsync(Guid id, bool confirmed, CancellationToken token = default);
}
public interface ITemplateService
{
    Task<IReadOnlyList<Template>> ListAsync(int offset = 0, int limit = 100, CancellationToken token = default);
    Task<TemplateDetails> GetAsync(Guid id, CancellationToken token = default);
    Task<DocumentSnapshot> LoadSnapshotAsync(Guid versionId, CancellationToken token = default);
    Task<TemplateDetails> ImportAsync(Guid? templateId, string name, string contractType, string version,
        string path, CancellationToken token = default);
    Task UpdateAsync(Guid id, string name, string contractType, string notes, bool enabled, CancellationToken token = default);
    Task SetCurrentAsync(Guid id, Guid versionId, CancellationToken token = default);
    Task RelinkAsync(Guid versionId, string path, CancellationToken token = default);
    Task<IReadOnlyList<ComparisonFile>> FindRelinkCandidatesAsync(TemplateVersion version, CancellationToken token = default);
    Task<TemplateReferences> ReferencesAsync(Guid id, CancellationToken token = default);
    Task DeleteAsync(Guid id, bool confirmed, CancellationToken token = default);
}
