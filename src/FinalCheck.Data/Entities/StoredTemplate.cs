namespace FinalCheck.Data.Entities;

public sealed class StoredTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ContractType { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
public sealed class StoredTemplateVersion
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public string Version { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime ModifiedAtUtc { get; set; }
    public string Sha256 { get; set; } = "";
    public Guid SnapshotId { get; set; }
    public bool IsCurrent { get; set; }
    public int ParseStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
