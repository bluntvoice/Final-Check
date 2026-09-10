namespace FinalCheck.Data.Entities;

public sealed class StoredDocumentSnapshot
{
    public Guid Id { get; set; }

    public int SnapshotSchemaVersion { get; set; }

    public byte[] Payload { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }
}
