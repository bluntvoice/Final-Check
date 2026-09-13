using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Management;
using FinalCheck.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;
public sealed class ProjectComparisonStore(FinalCheckDbContext db, IDocumentSnapshotSerializer snapshots, IComparisonResultSerializer comparisons) : IProjectComparisonStore
{
    public async Task SetBaselineAsync(Guid projectId, Guid versionId, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token); var project = await db.Projects.SingleAsync(x => x.Id == projectId, token);
        var version = await db.ContractVersions.AsNoTracking().SingleAsync(x => x.Id == versionId, token);
        if (project.Status != (int)ProjectStatus.Active || version.ProjectId != projectId || version.Role != (int)ContractVersionRole.Own) throw new ArgumentException("只有本活跃项目的我方版本可以成为当前我方基准。");
        project.CurrentBaselineVersionId = versionId; project.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(token); token.ThrowIfCancellationRequested(); await transaction.CommitAsync(CancellationToken.None);
    }
    public async Task<ProjectComparisonChoices> ChoicesAsync(Guid projectId, Guid currentVersionId, CancellationToken token = default)
    {
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == projectId, token); var current = await db.ContractVersions.AsNoTracking().SingleAsync(x => x.Id == currentVersionId, token);
        if (current.ProjectId != projectId) throw new ArgumentException("版本不属于当前项目。");
        var options = new List<ProjectBaselineOption>();
        if (project.CurrentBaselineVersionId is { } ownId)
        {
            var own = await db.ContractVersions.AsNoTracking().SingleAsync(x => x.Id == ownId, token);
            if (own.ProjectId != projectId || own.Role != (int)ContractVersionRole.Own) throw new InvalidDataException("项目当前基准身份无效。");
            options.Add(new(ProjectBaselineType.Own, own.Id, $"我方 · 第 {own.RoundNumber} 轮 · {own.FileName}", true));
        }
        if (project.BoundTemplateId is { } templateId)
        {
            var template = await db.Templates.AsNoTracking().SingleAsync(x => x.Id == templateId, token);
            foreach (var version in await db.TemplateVersions.AsNoTracking().Where(x => x.TemplateId == templateId).OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.CreatedAtUtc).ToArrayAsync(token))
                options.Add(new(ProjectBaselineType.Template, version.Id, $"模板 · {template.Name} · {version.Version}", version.IsCurrent));
        }
        var recommendation = current.Role == (int)ContractVersionRole.Counterparty && project.CurrentBaselineVersionId is not null ? "建议与当前我方基准比较，不与上一版对方版本比较。" : "请明确选择当前我方基准或模板当前/历史版本。";
        return new(projectId, currentVersionId, options, project.LastBaselineType is { } type ? (ProjectBaselineType)type : null, recommendation);
    }
    public async Task<ProjectComparisonInput> LoadInputAsync(ProjectComparisonSelection selection, CancellationToken token = default)
    {
        var choices = await ChoicesAsync(selection.ProjectId, selection.CurrentVersionId, token);
        var option = choices.Options.SingleOrDefault(x => x.Type == selection.BaselineType && x.VersionId == selection.BaselineVersionId) ?? throw new ArgumentException("基准已变化或不属于当前项目，请重新选择。");
        var versionStore = new ContractVersionStore(db, new DocumentSnapshotStore(db, snapshots)); var current = await versionStore.GetAsync(selection.CurrentVersionId, token); var currentSnapshot = await versionStore.LoadSnapshotAsync(selection.CurrentVersionId, token);
        if (selection.BaselineType == ProjectBaselineType.Own)
        {
            var baseline = await versionStore.GetAsync(selection.BaselineVersionId, token); return new(selection, baseline.Source, current.Source, await versionStore.LoadSnapshotAsync(baseline.ContractVersionId, token), currentSnapshot, option.Name);
        }
        var templateVersion = await db.TemplateVersions.AsNoTracking().SingleAsync(x => x.Id == selection.BaselineVersionId, token);
        var templateSnapshot = await new TemplateStore(db, new DocumentSnapshotStore(db, snapshots)).LoadSnapshotAsync(templateVersion.Id, token);
        return new(selection, new(templateVersion.FilePath, templateVersion.FileName, templateVersion.FileSize, templateVersion.ModifiedAtUtc, templateVersion.Sha256), current.Source, templateSnapshot, currentSnapshot, option.Name);
    }
    public async Task<ComparisonWorkflowResult> SaveAsync(ProjectComparisonInput input, ComparisonResult result, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token); var selection = input.Selection;
        var project = await db.Projects.SingleAsync(x => x.Id == selection.ProjectId, token);
        if (project.Status != (int)ProjectStatus.Active) throw new InvalidOperationException("请先恢复项目再进行比对。");
        var rechecked = await LoadInputAsync(selection, token);
        if (rechecked.BaselineFile.Sha256 != input.BaselineFile.Sha256 || rechecked.CurrentFile.Sha256 != input.CurrentFile.Sha256) throw new InvalidDataException("比对输入身份已变化。");
        var currentRow = await db.ContractVersions.AsNoTracking().SingleAsync(x => x.Id == selection.CurrentVersionId, token);
        var baselineSnapshot = selection.BaselineType == ProjectBaselineType.Own ? await db.ContractVersions.Where(x => x.Id == selection.BaselineVersionId).Select(x => x.SnapshotId).SingleAsync(token) : await db.TemplateVersions.Where(x => x.Id == selection.BaselineVersionId).Select(x => x.SnapshotId).SingleAsync(token);
        var saved = await new ComparisonRecordStore(db, snapshots, comparisons).SaveAsync(input.BaselineFile, input.CurrentFile, input.Baseline, input.Current, result, token);
        db.ProjectComparisons.Add(new() { RecordId = saved.Record.RecordId, ProjectId = selection.ProjectId, CurrentVersionId = selection.CurrentVersionId, BaselineType = (int)selection.BaselineType,
            OwnBaselineVersionId = selection.BaselineType == ProjectBaselineType.Own ? selection.BaselineVersionId : null, TemplateBaselineVersionId = selection.BaselineType == ProjectBaselineType.Template ? selection.BaselineVersionId : null,
            BaselineSourceSnapshotId = baselineSnapshot, CurrentSourceSnapshotId = currentRow.SnapshotId, BaselineName = input.BaselineName, CurrentName = input.CurrentFile.Name, TotalChanges = result.Changes.Count, CreatedAtUtc = saved.Record.CreatedAt.UtcDateTime });
        project.LastBaselineType = (int)selection.BaselineType; project.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(token); token.ThrowIfCancellationRequested(); await transaction.CommitAsync(CancellationToken.None); return saved;
    }
    public async Task<IReadOnlyList<ProjectComparisonHistory>> HistoryAsync(Guid projectId, Guid? versionId = null, int offset = 0, int limit = 20, CancellationToken token = default)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var links = db.ProjectComparisons.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (versionId is { } id) links = links.Where(x => x.CurrentVersionId == id || x.OwnBaselineVersionId == id);
        var rows = await (from link in links join record in db.ComparisonRecords.AsNoTracking() on link.RecordId equals record.Id orderby link.CreatedAtUtc descending, link.RecordId select new { Link = link, record.Payload }).Skip(offset).Take(limit).ToArrayAsync(token);
        return rows.Select(x =>
        {
            var record = ComparisonRecordStore.Decode(x.Payload); var link = x.Link;
            if (!Enum.IsDefined((ProjectBaselineType)link.BaselineType) || record.ReviewStates.Count != link.TotalChanges) throw new InvalidDataException("比对历史元数据不一致。");
            return new ProjectComparisonHistory(link.RecordId, link.ProjectId, link.CurrentVersionId, (ProjectBaselineType)link.BaselineType, link.OwnBaselineVersionId ?? link.TemplateBaselineVersionId ?? throw new InvalidDataException("缺失基准身份。"),
                link.CurrentName, link.BaselineName, link.CreatedAtUtc, link.TotalChanges, record.ReviewStates.Values.Count(x => x == ComparisonReviewState.Unresolved), record.ReviewStates.Values.Count(x => x == ComparisonReviewState.Confirmed), record.ReviewStates.Values.Count(x => x == ComparisonReviewState.Ignored));
        }).ToArray();
    }
}
