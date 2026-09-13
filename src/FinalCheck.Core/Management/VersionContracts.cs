using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Core.Management;

public enum ContractVersionRole { Own, Counterparty }
public sealed record NegotiationRound(Guid RoundId, Guid ProjectId, int Number);
public sealed record ContractVersion(Guid ContractVersionId, Guid ProjectId, ComparisonFile Source, Guid SnapshotId,
    ContractVersionRole Role, int RoundNumber, bool IsCurrentBaseline, DateTimeOffset ImportedAt, string Notes,
    ComparisonFile OriginalSourceMetadata, Guid? DuplicateReference, DocumentParseStatus ParseStatus);
public sealed record VersionImport(string Path, ContractVersionRole Role, int RoundNumber, string Notes, bool AllowDuplicate = false, string? ExpectedSha256 = null);
public sealed record PreparedVersion(VersionImport Request, ComparisonFile Source, DocumentSnapshot Snapshot);
public interface IContractVersionStore
{
    Task<IReadOnlyList<ContractVersion>> ListAsync(Guid projectId, bool chronological = false, int offset = 0, int limit = 20, CancellationToken token = default);
    Task<IReadOnlyList<NegotiationRound>> RoundsAsync(Guid projectId, CancellationToken token = default);
    Task<IReadOnlyList<ContractVersion>> SameContentAsync(Guid projectId, string sha256, CancellationToken token = default);
    Task<IReadOnlyList<ContractVersion>> ImportAsync(Guid projectId, IReadOnlyList<PreparedVersion> versions, CancellationToken token = default);
    Task<ContractVersion> GetAsync(Guid id, CancellationToken token = default);
    Task<DocumentSnapshot> LoadSnapshotAsync(Guid id, CancellationToken token = default);
    Task RelinkAsync(Guid id, ComparisonFile source, CancellationToken token = default);
}
public interface IContractVersionService
{
    Task<IReadOnlyList<ContractVersion>> ListAsync(Guid projectId, bool chronological = false, int offset = 0, int limit = 20, CancellationToken token = default);
    Task<IReadOnlyList<NegotiationRound>> RoundsAsync(Guid projectId, CancellationToken token = default);
    Task<IReadOnlyList<ContractVersion>> SameContentAsync(Guid projectId, string sha256, CancellationToken token = default);
    Task<ComparisonFile> InspectAsync(string path, CancellationToken token = default);
    Task<IReadOnlyList<ContractVersion>> ImportAsync(Guid projectId, IReadOnlyList<VersionImport> versions, CancellationToken token = default);
    Task<ContractVersion> GetAsync(Guid id, CancellationToken token = default);
    Task<DocumentSnapshot> LoadSnapshotAsync(Guid id, CancellationToken token = default);
    Task RelinkAsync(Guid id, string path, CancellationToken token = default);
    Task<IReadOnlyList<ComparisonFile>> FindRelinkCandidatesAsync(ContractVersion version, CancellationToken token = default);
}
