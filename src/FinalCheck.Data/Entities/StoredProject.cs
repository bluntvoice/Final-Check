namespace FinalCheck.Data.Entities;

public sealed class StoredProject
{
    public Guid Id { get; set; }
    public string ProjectName { get; set; } = "";
    public string Counterparty { get; set; } = "";
    public string ContractType { get; set; } = "";
    public Guid? BoundTemplateId { get; set; }
    public Guid? BoundTemplateVersionId { get; set; }
    public Guid? FolderId { get; set; }
    public string TagsJson { get; set; } = "[]";
    public int Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? CurrentBaselineVersionId { get; set; }
    public int? LastBaselineType { get; set; }
    public string Notes { get; set; } = "";
}
public sealed class StoredProjectFolder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
