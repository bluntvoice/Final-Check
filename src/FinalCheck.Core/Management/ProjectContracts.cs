namespace FinalCheck.Core.Management;

public enum ProjectStatus { Active, Archived, Recycled }
public enum ProjectBaselineType { Template, Own }
public sealed record ProjectTag(string Name, string Color);
public sealed record ProjectFolder(Guid FolderId, string Name);
public sealed record ContractProject(Guid ProjectId, string ProjectName, string Counterparty, string ContractType,
    Guid? BoundTemplateId, Guid? BoundTemplateVersionId, Guid? FolderId, IReadOnlyList<ProjectTag> Tags,
    ProjectStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid? CurrentBaselineVersionId,
    string Notes, ProjectBaselineType? LastBaselineType);
public sealed record ProjectListItem(ContractProject Project, string TemplateName, string TemplateVersion, string FolderName,
    string CurrentBaselineName = "尚未设置");
public sealed record ProjectQuery(string Search = "", Guid? TemplateId = null, string ContractType = "",
    DateTimeOffset? UpdatedSince = null, ProjectStatus Status = ProjectStatus.Active, Guid? FolderId = null, string Tag = "", int Offset = 0, int Limit = 20);
public sealed record ProjectEdit(string ProjectName, string Counterparty, string ContractType, Guid? TemplateId,
    Guid? TemplateVersionId, string FolderName, IReadOnlyList<ProjectTag> Tags, string Notes);
public interface IProjectStore
{
    Task<IReadOnlyList<ProjectListItem>> ListAsync(ProjectQuery query, CancellationToken token = default);
    Task<ContractProject> GetAsync(Guid id, CancellationToken token = default);
    Task<ContractProject> SaveAsync(Guid? id, ProjectEdit edit, CancellationToken token = default);
    Task<IReadOnlyList<ProjectFolder>> FoldersAsync(CancellationToken token = default);
}
public interface IProjectService
{
    Task<IReadOnlyList<ProjectListItem>> ListAsync(ProjectQuery query, CancellationToken token = default);
    Task<ContractProject> GetAsync(Guid id, CancellationToken token = default);
    Task<ContractProject> SaveAsync(Guid? id, ProjectEdit edit, CancellationToken token = default);
    Task<IReadOnlyList<ProjectFolder>> FoldersAsync(CancellationToken token = default);
}
