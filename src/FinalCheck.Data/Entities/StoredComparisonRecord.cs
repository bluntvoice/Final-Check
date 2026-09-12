namespace FinalCheck.Data.Entities;

public sealed class StoredComparisonRecord
{
    public Guid Id { get; set; }
    public Guid BaselineSnapshotId { get; set; }
    public Guid CurrentSnapshotId { get; set; }
    public Guid ResultId { get; set; }
    public byte[] Payload { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
}
