namespace FinalCheck.Data.Entities;

public sealed class StoredComparisonResult
{
    public Guid Id { get; set; }

    public int ComparisonSchemaVersion { get; set; }

    public string BaselineSnapshotId { get; set; } = string.Empty;

    public string CurrentSnapshotId { get; set; } = string.Empty;

    public string AlgorithmVersion { get; set; } = string.Empty;

    public byte[] Payload { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }
}
