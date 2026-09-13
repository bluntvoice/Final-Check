namespace FinalCheck.Data.Entities;
public sealed class StoredProjectDeletion
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Status { get; set; } = "PendingCleanup";
    public byte[] Payload { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
}
