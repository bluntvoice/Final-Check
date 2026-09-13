using System.Text.Json;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Management;
using FinalCheck.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;
public sealed class ProjectLifecycleStore(FinalCheckDbContext db, IDocumentSnapshotSerializer snapshots, IComparisonResultSerializer comparisons) : IProjectLifecycleStore
{
    public async Task<ProjectSummary> SummaryAsync(Guid projectId, CancellationToken token = default)
    {
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == projectId, token);
        var baseline = project.CurrentBaselineVersionId is { } own ? await db.ContractVersions.Where(x => x.Id == own).Select(x => x.FileName).SingleAsync(token) : "尚未设置当前我方基准";
        var latest = await db.ContractVersions.Where(x => x.ProjectId == projectId).OrderByDescending(x => x.ImportedAtUtc).Select(x => x.FileName).FirstOrDefaultAsync(token) ?? "尚无版本";
        var history = await new ProjectComparisonStore(db, snapshots, comparisons).HistoryAsync(projectId, limit: 1, token: token);
        var versionIds = db.ContractVersions.Where(x => x.ProjectId == projectId).Select(x => x.Id);
        var pending = await db.FormatRestoreOperations.CountAsync(x => versionIds.Contains(x.ContractVersionId) && x.Status == "Prepared", token);
        return new(projectId, (ProjectStatus)project.Status, baseline, latest, history.Count > 0 ? history[0] : null, pending);
    }
    public async Task<VersionRestoreState> RestoreStateAsync(Guid versionId, CancellationToken token = default)
    {
        var copy = await new FormatRestoreStore(db, snapshots, comparisons).LoadWorkingCopyAsync(versionId, token);
        var pending = await db.FormatRestoreOperations.CountAsync(x => x.ContractVersionId == versionId && x.Status == "Prepared", token);
        return copy is null ? new("尚无恢复 Working Copy。", null, null, pending) : new($"Working Copy · {copy.UpdatedAt:u} · {copy.Sha256}", copy.WorkingPath, copy.Sha256, pending);
    }
    public async Task SetStatusAsync(Guid projectId, ProjectStatus status, CancellationToken token = default)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentException("未知项目状态。");
        await using var transaction = await db.Database.BeginTransactionAsync(token); var project = await db.Projects.SingleAsync(x => x.Id == projectId, token);
        if (project.Status == (int)ProjectStatus.Recycled && status == ProjectStatus.Archived) throw new ArgumentException("请先从回收站恢复项目。");
        project.Status = (int)status; project.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(token); token.ThrowIfCancellationRequested(); await transaction.CommitAsync(CancellationToken.None);
    }
    public async Task<IReadOnlyList<Guid>> VersionIdsAsync(Guid projectId, CancellationToken token = default) => await db.ContractVersions.Where(x => x.ProjectId == projectId).Select(x => x.Id).ToArrayAsync(token);
    public static ProjectDeletionRecord Decode(Entities.StoredProjectDeletion row)
    {
        var record = JsonSerializer.Deserialize<ProjectDeletionRecord>(row.Payload) ?? throw new InvalidDataException("Invalid deletion journal.");
        if (record.SchemaVersion != 1 || record.OperationId != row.Id || record.ProjectId != row.ProjectId || record.Status != row.Status || record.Status is not ("PendingCleanup" or "Completed" or "NeedsReview") ||
            record.VersionIds.Any(id => id == Guid.Empty) || record.OriginalPaths.Any(p => !Path.IsPathFullyQualified(p))) throw new InvalidDataException("Unknown deletion journal identity/schema.");
        return record;
    }
    public async Task<ProjectDeletionRecord> DeleteAsync(Guid projectId, bool confirmed, IReadOnlyList<Guid> lockedVersions, IDataRootProvider paths, CancellationToken token = default)
    {
        if (!confirmed) throw new ArgumentException("永久删除必须二次明确确认。");
        await using var transaction = await db.Database.BeginTransactionAsync(token); var project = await db.Projects.SingleAsync(x => x.Id == projectId, token);
        if (project.Status != (int)ProjectStatus.Recycled) throw new ArgumentException("只有回收站项目可以永久删除。");
        var versions = await db.ContractVersions.Where(x => x.ProjectId == projectId).ToArrayAsync(token); var ids = versions.Select(x => x.Id).ToArray();
        if (!ids.ToHashSet().SetEquals(lockedVersions) || await db.FormatRestoreOperations.AnyAsync(x => ids.Contains(x.ContractVersionId) && x.Status == "Prepared", token)) throw new InvalidOperationException("版本或恢复操作正在变化，请完成恢复后重试。");
        var originals = versions.SelectMany(v => { var version = ContractVersionStore.Map(v); return new[] { version.Source.Path, version.OriginalSourceMetadata.Path }; }).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var cleanup = new Dictionary<string, ManagedProjectCleanupFile>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        void AddFile(Guid versionId, string path, string? hash, string expectedName)
        {
            if (path.Length == 0) return;
            var expected = Path.Combine(paths.WorkingCopyPath, versionId.ToString("N"), expectedName);
            if (Path.GetFullPath(path) != Path.GetFullPath(expected) || hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit) || originals.Contains(path)) throw new InvalidDataException("恢复文件路径/身份不安全，拒绝永久删除。");
            var relative = Path.GetRelativePath(paths.CurrentDataRoot, path); cleanup[relative] = new(relative, hash);
        }
        foreach (var versionId in ids)
        {
            var copy = await new FormatRestoreStore(db, snapshots, comparisons).LoadWorkingCopyAsync(versionId, token);
            if (copy is not null) { originals.Add(copy.OriginalPath); AddFile(versionId, copy.WorkingPath, copy.Sha256, "restored.docx"); }
        }
        foreach (var row in await db.FormatRestoreOperations.Where(x => ids.Contains(x.ContractVersionId)).ToArrayAsync(token))
        {
            var operation = FormatRestoreStore.Decode(row); originals.Add(operation.WorkingCopy.OriginalPath);
            if (operation.BackupPath.Length > 0 && operation.BeforeSha256 is not null) AddFile(row.ContractVersionId, operation.BackupPath, operation.BeforeSha256, $"previous-{row.Id:N}.docx");
            if (operation.TemporaryPath.Length > 0) AddFile(row.ContractVersionId, operation.TemporaryPath, operation.AfterSha256, $"candidate-{row.Id:N}.docx");
        }
        // Original identities of comparisons remain excluded from later storage migration even after deletion.
        var links = await db.ProjectComparisons.Where(x => x.ProjectId == projectId).ToArrayAsync(token); var recordIds = links.Select(x => x.RecordId).ToArray();
        var records = await db.ComparisonRecords.Where(x => recordIds.Contains(x.Id)).ToArrayAsync(token); var resultIds = records.Select(x => x.ResultId).ToArray();
        foreach (var row in records) { var record = ComparisonRecordStore.Decode(row.Payload); originals.Add(record.BaselineFile.Path); originals.Add(record.CurrentFile.Path); }
        if (cleanup.Values.Any(f => originals.Contains(Path.Combine(paths.CurrentDataRoot, f.RelativePath)))) throw new InvalidDataException("原始 DOCX 与待清理文件重叠。");
        var candidates = versions.Select(x => x.SnapshotId).Concat(records.SelectMany(x => new[] { x.BaselineSnapshotId, x.CurrentSnapshotId })).Distinct().ToArray();
        var journal = new ProjectDeletionRecord(1, Guid.NewGuid(), projectId, ids, cleanup.Values.ToArray(), originals.ToArray(), DateTimeOffset.UtcNow, "PendingCleanup", "");
        db.ProjectDeletionOperations.Add(new() { Id = journal.OperationId, ProjectId = projectId, Payload = JsonSerializer.SerializeToUtf8Bytes(journal), Status = journal.Status, CreatedAtUtc = journal.CreatedAt.UtcDateTime });
        await db.SaveChangesAsync(token);
        await db.ProjectComparisons.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync(token);
        await db.ComparisonRecords.Where(x => recordIds.Contains(x.Id)).ExecuteDeleteAsync(token);
        await db.ComparisonResults.Where(x => resultIds.Contains(x.Id) && !db.ComparisonRecords.Any(r => r.ResultId == x.Id)).ExecuteDeleteAsync(token);
        await db.RestoredWorkingCopies.Where(x => ids.Contains(x.ContractVersionId)).ExecuteDeleteAsync(token);
        await db.FormatRestoreOperations.Where(x => ids.Contains(x.ContractVersionId)).ExecuteDeleteAsync(token);
        await db.ContractVersions.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync(token);
        await db.NegotiationRounds.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync(token);
        await db.Projects.Where(x => x.Id == projectId).ExecuteDeleteAsync(token);
        foreach (var snapshotId in candidates)
        {
            if (await db.TemplateVersions.AnyAsync(x => x.SnapshotId == snapshotId, token) || await db.ContractVersions.AnyAsync(x => x.SnapshotId == snapshotId, token) ||
                await db.ComparisonRecords.AnyAsync(x => x.BaselineSnapshotId == snapshotId || x.CurrentSnapshotId == snapshotId, token) || await db.ProjectComparisons.AnyAsync(x => x.BaselineSourceSnapshotId == snapshotId || x.CurrentSourceSnapshotId == snapshotId, token)) continue;
            var row = await db.DocumentSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == snapshotId, token); if (row is null) continue;
            var hashId = "sha256:" + snapshots.Deserialize(row.Payload).Metadata.Sha256;
            // Older independent/restore result rows have hash identities rather than Guid FKs: conservatively retain shared content.
            if (await db.ComparisonResults.AnyAsync(x => x.BaselineSnapshotId == hashId || x.CurrentSnapshotId == hashId, token)) continue;
            await db.DocumentSnapshots.Where(x => x.Id == snapshotId).ExecuteDeleteAsync(token);
        }
        token.ThrowIfCancellationRequested(); await transaction.CommitAsync(CancellationToken.None); db.ChangeTracker.Clear(); return journal;
    }
    public async Task<IReadOnlyList<ProjectDeletionRecord>> PendingCleanupAsync(CancellationToken token = default) =>
        (await db.ProjectDeletionOperations.AsNoTracking().Where(x => x.Status != "Completed").OrderBy(x => x.CreatedAtUtc).ToArrayAsync(token)).Select(Decode).ToArray();
    public async Task SaveCleanupAsync(ProjectDeletionRecord record, CancellationToken token = default)
    { await db.ProjectDeletionOperations.Where(x => x.Id == record.OperationId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, record.Status).SetProperty(x => x.Payload, JsonSerializer.SerializeToUtf8Bytes(record)), token); }
}
