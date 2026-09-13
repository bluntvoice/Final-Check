using System.Text.Json;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Management;
using FinalCheck.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;

public sealed class ContractVersionStore(FinalCheckDbContext db, DocumentSnapshotStore snapshots) : IContractVersionStore
{
    public static ContractVersion Map(StoredContractVersion row, Guid? baseline = null)
    {
        if (!Enum.IsDefined((ContractVersionRole)row.Role) || !Enum.IsDefined((DocumentParseStatus)row.ParseStatus) || row.RoundNumber < 1 || !Path.IsPathFullyQualified(row.FilePath)) throw new InvalidDataException("无效合同版本元数据。");
        var original = JsonSerializer.Deserialize<ComparisonFile>(row.OriginalSourceJson) ?? throw new InvalidDataException("源文件元数据缺失。");
        if (original.Sha256 != row.Sha256 || !Path.IsPathFullyQualified(original.Path)) throw new InvalidDataException("源文件身份不一致。");
        return new(row.Id, row.ProjectId, new(row.FilePath, row.FileName, row.FileSize, row.ModifiedAtUtc, row.Sha256), row.SnapshotId,
            (ContractVersionRole)row.Role, row.RoundNumber, baseline == row.Id, row.ImportedAtUtc, row.Notes, original, row.DuplicateReference, (DocumentParseStatus)row.ParseStatus);
    }
    public async Task<IReadOnlyList<ContractVersion>> ListAsync(Guid projectId, bool chronological = false, int offset = 0, int limit = 20, CancellationToken token = default)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var baseline = await db.Projects.Where(x => x.Id == projectId).Select(x => x.CurrentBaselineVersionId).SingleAsync(token);
        var query = db.ContractVersions.AsNoTracking().Where(x => x.ProjectId == projectId);
        var ordered = chronological ? query.OrderBy(x => x.ImportedAtUtc).ThenBy(x => x.Id) : query.OrderBy(x => x.RoundNumber).ThenBy(x => x.ImportedAtUtc).ThenBy(x => x.Id);
        return (await ordered.Skip(offset).Take(limit).ToArrayAsync(token)).Select(x => Map(x, baseline)).ToArray();
    }
    public async Task<IReadOnlyList<NegotiationRound>> RoundsAsync(Guid projectId, CancellationToken token = default) =>
        (await db.NegotiationRounds.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Number).ToArrayAsync(token)).Select(x => new NegotiationRound(x.Id, x.ProjectId, x.Number)).ToArray();
    public async Task<IReadOnlyList<ContractVersion>> SameContentAsync(Guid projectId, string sha256, CancellationToken token = default) =>
        (await db.ContractVersions.AsNoTracking().Where(x => x.ProjectId == projectId && x.Sha256 == sha256).OrderBy(x => x.ImportedAtUtc).Take(100).ToArrayAsync(token)).Select(x => Map(x)).ToArray();
    public async Task<ContractVersion> GetAsync(Guid id, CancellationToken token = default)
    {
        var row = await db.ContractVersions.AsNoTracking().SingleAsync(x => x.Id == id, token);
        return Map(row, await db.Projects.Where(x => x.Id == row.ProjectId).Select(x => x.CurrentBaselineVersionId).SingleAsync(token));
    }
    public async Task<DocumentSnapshot> LoadSnapshotAsync(Guid id, CancellationToken token = default)
    {
        var version = await GetAsync(id, token); var snapshot = await snapshots.LoadAsync(version.SnapshotId, token) ?? throw new InvalidDataException("版本快照缺失。");
        if (snapshot.Metadata.Sha256 != version.Source.Sha256) throw new InvalidDataException("版本快照 hash 不一致。"); return snapshot;
    }
    public async Task<IReadOnlyList<ContractVersion>> ImportAsync(Guid projectId, IReadOnlyList<PreparedVersion> versions, CancellationToken token = default)
    {
        if (versions.Count is < 1 or > 100) throw new ArgumentException("一次导入 1–100 份版本。");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var project = await db.Projects.SingleAsync(x => x.Id == projectId, token);
        if (project.Status != (int)ProjectStatus.Active) throw new InvalidOperationException("请先恢复项目再导入版本。");
        var rounds = (await db.NegotiationRounds.Where(x => x.ProjectId == projectId).ToArrayAsync(token)).ToList();
        var added = new List<StoredContractVersion>(); var now = DateTime.UtcNow;
        foreach (var prepared in versions)
        {
            token.ThrowIfCancellationRequested(); var request = prepared.Request; var file = prepared.Source;
            if (!Enum.IsDefined(request.Role) || request.RoundNumber < 1 || request.RoundNumber > 10000 || file.Sha256 != prepared.Snapshot.Metadata.Sha256 || !Path.IsPathFullyQualified(file.Path)) throw new ArgumentException("明确选择合法角色和轮次，且文件与快照身份必须一致。");
            var duplicate = added.FirstOrDefault(x => x.Sha256 == file.Sha256)?.Id ?? await db.ContractVersions.Where(x => x.ProjectId == projectId && x.Sha256 == file.Sha256).OrderBy(x => x.ImportedAtUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(token);
            if (duplicate is not null && !request.AllowDuplicate) throw new InvalidOperationException("已存在内容完全相同的版本，请跳过或明确选择仍然导入。");
            if (!rounds.Any(x => x.Number == request.RoundNumber))
            {
                var max = rounds.Count == 0 ? 0 : rounds.Max(x => x.Number);
                if (request.RoundNumber != max + 1) throw new ArgumentException("请选择已有轮次或下一轮，不能跳过轮次。");
                var round = new StoredNegotiationRound { Id = Guid.NewGuid(), ProjectId = projectId, Number = request.RoundNumber }; db.NegotiationRounds.Add(round); rounds.Add(round);
            }
            var snapshotId = await snapshots.SaveAsync(prepared.Snapshot, token);
            var row = new StoredContractVersion { Id = Guid.NewGuid(), ProjectId = projectId, FilePath = file.Path, FileName = file.Name, FileSize = file.Size, ModifiedAtUtc = file.ModifiedAt.UtcDateTime,
                Sha256 = file.Sha256, SnapshotId = snapshotId, Role = (int)request.Role, RoundNumber = request.RoundNumber, ImportedAtUtc = now.AddTicks(added.Count), Notes = request.Notes,
                OriginalSourceJson = JsonSerializer.Serialize(file), DuplicateReference = duplicate, ParseStatus = (int)prepared.Snapshot.ParseStatus };
            db.ContractVersions.Add(row); added.Add(row);
        }
        project.UpdatedAtUtc = now; await db.SaveChangesAsync(token); token.ThrowIfCancellationRequested(); await transaction.CommitAsync(CancellationToken.None);
        return added.Select(x => Map(x, project.CurrentBaselineVersionId)).ToArray();
    }
    public async Task RelinkAsync(Guid id, ComparisonFile source, CancellationToken token = default)
    {
        var row = await db.ContractVersions.SingleAsync(x => x.Id == id, token);
        if (!source.Sha256.Equals(row.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("文件内容已变化，请作为新版本导入；旧 Snapshot 不覆盖。");
        row.FilePath = source.Path; row.FileName = source.Name; row.FileSize = source.Size; row.ModifiedAtUtc = source.ModifiedAt.UtcDateTime; await db.SaveChangesAsync(token);
    }
}
