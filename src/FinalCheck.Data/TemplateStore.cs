using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Management;
using Microsoft.EntityFrameworkCore;
using FinalCheck.Data.Entities;

namespace FinalCheck.Data;

public sealed class TemplateStore(FinalCheckDbContext db, DocumentSnapshotStore snapshots) : ITemplateStore
{
    private static Template Map(StoredTemplate row) => new(row.Id, row.Name, row.ContractType, row.IsEnabled, row.IsDeleted,
        row.CreatedAtUtc, row.UpdatedAtUtc, row.Notes);
    private static TemplateVersion Map(StoredTemplateVersion row) => new(row.Id, row.TemplateId, row.Version,
        new(row.FilePath, row.FileName, row.FileSize, row.ModifiedAtUtc, row.Sha256), row.SnapshotId, row.IsCurrent,
        row.CreatedAtUtc, (DocumentParseStatus)row.ParseStatus);
    public async Task<IReadOnlyList<Template>> ListAsync(int offset = 0, int limit = 100, CancellationToken token = default)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return (await db.Templates.AsNoTracking().Where(x => !x.IsDeleted).OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id)
            .Skip(offset).Take(limit).ToArrayAsync(token)).Select(Map).ToArray();
    }
    public async Task<TemplateDetails> GetAsync(Guid id, CancellationToken token = default)
    {
        var row = await db.Templates.AsNoTracking().SingleAsync(x => x.Id == id, token);
        return new(Map(row), (await db.TemplateVersions.AsNoTracking().Where(x => x.TemplateId == id)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToArrayAsync(token)).Select(Map).ToArray());
    }
    public async Task<DocumentSnapshot> LoadSnapshotAsync(Guid versionId, CancellationToken token = default)
    {
        var row = await db.TemplateVersions.AsNoTracking().SingleAsync(x => x.Id == versionId, token);
        var snapshot = await snapshots.LoadAsync(row.SnapshotId, token) ?? throw new InvalidDataException("模板历史快照缺失。");
        if (snapshot.Metadata.Sha256 != row.Sha256) throw new InvalidDataException("模板历史快照身份不一致。");
        return snapshot;
    }
    public async Task<TemplateDetails> AddVersionAsync(Guid? templateId, string name, string contractType, string version,
        ComparisonFile source, DocumentSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (source.Sha256 != snapshot.Metadata.Sha256 || !Path.IsPathFullyQualified(source.Path)) throw new InvalidDataException("模板文件与快照身份不一致。");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var now = DateTime.UtcNow;
        var row = templateId is { } existing ? await db.Templates.SingleAsync(x => x.Id == existing && !x.IsDeleted, token) :
            new StoredTemplate { Id = Guid.NewGuid(), Name = name.Trim(), ContractType = contractType.Trim(), CreatedAtUtc = now, UpdatedAtUtc = now };
        if (templateId is null) db.Templates.Add(row);
        if (await db.TemplateVersions.AnyAsync(x => x.TemplateId == row.Id && x.Version == version.Trim(), token)) throw new InvalidOperationException("模板版本号已存在，请修改版本号。");
        var snapshotId = await snapshots.SaveAsync(snapshot, token);
        db.TemplateVersions.Add(new() { Id = Guid.NewGuid(), TemplateId = row.Id, Version = version.Trim(), FilePath = source.Path,
            FileName = source.Name, FileSize = source.Size, ModifiedAtUtc = source.ModifiedAt.UtcDateTime, Sha256 = source.Sha256,
            SnapshotId = snapshotId, IsCurrent = templateId is null, ParseStatus = (int)snapshot.ParseStatus, CreatedAtUtc = now });
        row.UpdatedAtUtc = now;
        await db.SaveChangesAsync(token); token.ThrowIfCancellationRequested(); await transaction.CommitAsync(CancellationToken.None);
        return await GetAsync(row.Id, CancellationToken.None);
    }
    public async Task UpdateAsync(Guid id, string name, string contractType, string notes, bool enabled, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var changed = await db.Templates.Where(x => x.Id == id && !x.IsDeleted).ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, name.Trim())
            .SetProperty(x => x.ContractType, contractType.Trim()).SetProperty(x => x.Notes, notes).SetProperty(x => x.IsEnabled, enabled)
            .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), token);
        if (changed != 1) throw new InvalidOperationException("模板不存在或已删除。");
    }
    public async Task SetCurrentAsync(Guid id, Guid versionId, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await db.Templates.AnyAsync(x => x.Id == id && !x.IsDeleted, token) || !await db.TemplateVersions.AnyAsync(x => x.Id == versionId && x.TemplateId == id, token))
            throw new InvalidOperationException("模板版本不属于当前模板。");
        await db.TemplateVersions.Where(x => x.TemplateId == id && x.IsCurrent).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsCurrent, false), token);
        await db.TemplateVersions.Where(x => x.Id == versionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsCurrent, true), token);
        await db.Templates.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), token);
        token.ThrowIfCancellationRequested(); await transaction.CommitAsync(CancellationToken.None);
    }
    public async Task RelinkAsync(Guid versionId, ComparisonFile source, CancellationToken token = default)
    {
        var row = await db.TemplateVersions.SingleAsync(x => x.Id == versionId, token);
        if (!source.Sha256.Equals(row.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("文件内容已变化，请作为新模板版本导入；旧快照保持不变。");
        row.FilePath = source.Path; row.FileName = source.Name; row.FileSize = source.Size; row.ModifiedAtUtc = source.ModifiedAt.UtcDateTime;
        await db.SaveChangesAsync(token);
    }
    public async Task<TemplateReferences> ReferencesAsync(Guid id, CancellationToken token = default)
    {
        var snapshotIds = await db.TemplateVersions.Where(x => x.TemplateId == id).Select(x => x.SnapshotId).ToArrayAsync(token);
        return new([], await db.ComparisonRecords.CountAsync(x => snapshotIds.Contains(x.BaselineSnapshotId), token));
    }
    public async Task DeleteAsync(Guid id, bool confirmed, CancellationToken token = default)
    {
        if (!confirmed) throw new InvalidOperationException("删除模板需要确认；历史版本与快照将保留。");
        var changed = await db.Templates.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)
            .SetProperty(x => x.IsEnabled, false).SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), token);
        if (changed != 1) throw new InvalidOperationException("模板不存在。");
    }
}
