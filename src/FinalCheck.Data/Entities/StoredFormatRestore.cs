namespace FinalCheck.Data.Entities;

public sealed class StoredRestoredWorkingCopy
{
    public Guid ContractVersionId { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class StoredFormatRestoreOperation
{
    public Guid Id { get; set; }
    public Guid ContractVersionId { get; set; }
    public int SchemaVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
}
