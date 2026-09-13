namespace FinalCheck.Data.Entities;
public sealed class StoredProjectComparison
{
    public Guid RecordId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid CurrentVersionId { get; set; }
    public int BaselineType { get; set; }
    public Guid? OwnBaselineVersionId { get; set; }
    public Guid? TemplateBaselineVersionId { get; set; }
    public Guid BaselineSourceSnapshotId { get; set; }
    public Guid CurrentSourceSnapshotId { get; set; }
    public string BaselineName { get; set; } = "";
    public string CurrentName { get; set; } = "";
    public int TotalChanges { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
