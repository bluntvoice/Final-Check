using System.Text.Json;
using FinalCheck.Core.Management;
using FinalCheck.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;

public sealed class ProjectStore(FinalCheckDbContext db) : IProjectStore
{
    public static ContractProject Map(StoredProject row)
    {
        if (!Enum.IsDefined((ProjectStatus)row.Status) || row.LastBaselineType is { } type && !Enum.IsDefined((ProjectBaselineType)type)) throw new InvalidDataException("未知项目状态。");
        var tags = JsonSerializer.Deserialize<ProjectTag[]>(row.TagsJson) ?? throw new InvalidDataException("标签数据不完整。");
        ValidateTags(tags);
        return new(row.Id, row.ProjectName, row.Counterparty, row.ContractType, row.BoundTemplateId, row.BoundTemplateVersionId,
            row.FolderId, tags, (ProjectStatus)row.Status, row.CreatedAtUtc, row.UpdatedAtUtc, row.CurrentBaselineVersionId, row.Notes,
            row.LastBaselineType is { } value ? (ProjectBaselineType)value : null);
    }
    private static void ValidateTags(IReadOnlyList<ProjectTag> tags)
    {
        if (tags.Count > 50 || tags.Any(t => t is null || string.IsNullOrWhiteSpace(t.Name) || t.Color is null || t.Color.Length != 7 || t.Color[0] != '#' || !t.Color[1..].All(Uri.IsHexDigit)))
            throw new ArgumentException("标签必须有名称和 #RRGGBB 颜色，最多 50 个。");
        if (tags.Select(t => t.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tags.Count) throw new ArgumentException("标签名称不能重复。");
    }
    public async Task<IReadOnlyList<ProjectListItem>> ListAsync(ProjectQuery query, CancellationToken token = default)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > 100 || !Enum.IsDefined(query.Status)) throw new ArgumentOutOfRangeException(nameof(query));
        var rows = db.Projects.AsNoTracking().Where(x => x.Status == (int)query.Status);
        if (!string.IsNullOrWhiteSpace(query.Search)) rows = rows.Where(x => x.ProjectName.Contains(query.Search));
        if (query.TemplateId is { } templateFilterId) rows = rows.Where(x => x.BoundTemplateId == templateFilterId);
        if (!string.IsNullOrWhiteSpace(query.ContractType)) rows = rows.Where(x => x.ContractType.Contains(query.ContractType));
        if (query.UpdatedSince is { } since) { var utc = since.UtcDateTime; rows = rows.Where(x => x.UpdatedAtUtc >= utc); }
        if (query.FolderId is { } folderFilterId) rows = rows.Where(x => x.FolderId == folderFilterId);
        // Tag search uses an exact JSON name token, not substring matching another tag's name/color.
        if (!string.IsNullOrWhiteSpace(query.Tag)) { var fragment = "\"Name\":" + JsonSerializer.Serialize(query.Tag.Trim()); rows = rows.Where(x => x.TagsJson.Contains(fragment)); }
        var page = await (from row in rows
            join template in db.Templates.AsNoTracking() on row.BoundTemplateId equals template.Id into templates
            from template in templates.DefaultIfEmpty()
            join version in db.TemplateVersions.AsNoTracking() on row.BoundTemplateVersionId equals version.Id into versions
            from version in versions.DefaultIfEmpty()
            join folder in db.ProjectFolders.AsNoTracking() on row.FolderId equals folder.Id into folders
            from folder in folders.DefaultIfEmpty()
            join baseline in db.ContractVersions.AsNoTracking() on row.CurrentBaselineVersionId equals baseline.Id into baselines
            from baseline in baselines.DefaultIfEmpty()
            orderby row.UpdatedAtUtc descending, row.Id
            select new { Row = row, Template = template == null ? "未绑定模板" : template.Name, Version = version == null ? "" : version.Version, Folder = folder == null ? "" : folder.Name, Baseline = baseline == null ? "尚未设置当前我方基准" : baseline.FileName })
            .Skip(query.Offset).Take(query.Limit).ToArrayAsync(token);
        return page.Select(x => new ProjectListItem(Map(x.Row), x.Template, x.Version, x.Folder, x.Baseline)).ToArray();
    }
    public async Task<ContractProject> GetAsync(Guid id, CancellationToken token = default) => Map(await db.Projects.AsNoTracking().SingleAsync(x => x.Id == id, token));
    public async Task<ContractProject> SaveAsync(Guid? id, ProjectEdit edit, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edit.ProjectName); ValidateTags(edit.Tags);
        if ((edit.TemplateId is null) != (edit.TemplateVersionId is null)) throw new ArgumentException("模板及版本必须同时指定或解绑。");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var now = DateTime.UtcNow;
        var row = id is { } existing ? await db.Projects.SingleAsync(x => x.Id == existing, token) : new StoredProject { Id = Guid.NewGuid(), CreatedAtUtc = now };
        var sameBinding = id is not null && row.BoundTemplateId == edit.TemplateId && row.BoundTemplateVersionId == edit.TemplateVersionId;
        string? inheritedType = null;
        if (edit.TemplateId is { } templateId)
        {
            var template = await db.Templates.AsNoTracking().SingleAsync(x => x.Id == templateId, token);
            if (!await db.TemplateVersions.AnyAsync(x => x.Id == edit.TemplateVersionId && x.TemplateId == templateId, token)) throw new ArgumentException("版本不属于所选模板。");
            if ((!template.IsEnabled || template.IsDeleted) && !sameBinding) throw new InvalidOperationException("模板已停用或删除，不能新绑定；既有历史保留。");
            inheritedType = template.ContractType;
        }
        Guid? folderId = null;
        if (!string.IsNullOrWhiteSpace(edit.FolderName))
        {
            var name = edit.FolderName.Trim(); var folder = await db.ProjectFolders.SingleOrDefaultAsync(x => x.Name == name, token);
            if (folder is null) { folder = new() { Id = Guid.NewGuid(), Name = name }; db.ProjectFolders.Add(folder); }
            folderId = folder.Id;
        }
        if (id is null) db.Projects.Add(row);
        row.ProjectName = edit.ProjectName.Trim(); row.Counterparty = edit.Counterparty.Trim(); row.ContractType = string.IsNullOrWhiteSpace(edit.ContractType) ? inheritedType ?? "" : edit.ContractType.Trim();
        row.BoundTemplateId = edit.TemplateId; row.BoundTemplateVersionId = edit.TemplateVersionId; row.FolderId = folderId;
        row.TagsJson = JsonSerializer.Serialize(edit.Tags.Select(t => t with { Name = t.Name.Trim(), Color = t.Color.ToUpperInvariant() })); row.Notes = edit.Notes; row.UpdatedAtUtc = now;
        await db.SaveChangesAsync(token); token.ThrowIfCancellationRequested(); await transaction.CommitAsync(CancellationToken.None); return Map(row);
    }
    public async Task<IReadOnlyList<ProjectFolder>> FoldersAsync(CancellationToken token = default) =>
        (await db.ProjectFolders.AsNoTracking().OrderBy(x => x.Name).Take(100).ToArrayAsync(token)).Select(x => new ProjectFolder(x.Id, x.Name)).ToArray();
}
