namespace FinalCheck.Data.Entities;

public sealed class StoredNegotiationRound
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public int Number { get; set; }
}
public sealed class StoredContractVersion
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime ModifiedAtUtc { get; set; }
    public string Sha256 { get; set; } = "";
    public Guid SnapshotId { get; set; }
    public int Role { get; set; }
    public int RoundNumber { get; set; }
    public DateTime ImportedAtUtc { get; set; }
    public string Notes { get; set; } = "";
    public string OriginalSourceJson { get; set; } = "";
    public Guid? DuplicateReference { get; set; }
    public int ParseStatus { get; set; }
}
