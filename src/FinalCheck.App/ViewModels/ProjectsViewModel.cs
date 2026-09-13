using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinalCheck.Core.Management;

namespace FinalCheck.App.ViewModels;

public partial class ProjectsViewModel(IProjectService? service = null, ITemplateService? templateService = null, VersionManagementViewModel? versions = null) : ViewModelBase
{
    public VersionManagementViewModel Versions { get; } = versions ?? new();
    public ObservableCollection<ProjectListItem> Projects { get; } = [];
    public ObservableCollection<Template> Templates { get; } = [];
    public ObservableCollection<ProjectFolder> Folders { get; } = [];
    public ObservableCollection<ProjectTag> Tags { get; } = [];
    public IReadOnlyList<string> TimeOptions { get; } = ["全部时间", "最近 7 天", "最近 30 天"];
    public IReadOnlyList<string> TagColors { get; } = ["#2874A6", "#148F77", "#AF7AC5", "#D68910", "#C0392B"];
    [ObservableProperty] private ProjectListItem? selectedProject;
    [ObservableProperty] private Template? boundTemplate;
    [ObservableProperty] private Template? filterTemplate;
    [ObservableProperty] private ProjectFolder? filterFolder;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string filterType = "";
    [ObservableProperty] private string filterTag = "";
    [ObservableProperty] private string timeFilter = "全部时间";
    [ObservableProperty] private string projectName = "";
    [ObservableProperty] private string counterparty = "";
    [ObservableProperty] private string contractType = "";
    [ObservableProperty] private string folderName = "";
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string tagName = "";
    [ObservableProperty] private string tagColor = "#2874A6";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private string message = "尚未创建合同项目。";
    public bool CanEdit => !IsBusy;
    private Guid? boundVersionId;
    private Guid? editingProjectId;
    private ProjectQuery activeQuery = new();
    private bool loadingSelection;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanEdit));
    partial void OnBoundTemplateChanged(Template? value)
    {
        if (loadingSelection) return; boundVersionId = null;
        if (value is not null && string.IsNullOrWhiteSpace(ContractType)) ContractType = value.ContractType;
    }
    public async Task SelectProjectAsync(ProjectListItem item)
    {
        if (IsBusy || Versions.IsBusy || service is null) return;
        await PerformAsync(async () =>
        {
            var project = await service.GetAsync(item.Project.ProjectId); SelectedProject = item;
            editingProjectId = project.ProjectId;
            ProjectName = project.ProjectName; Counterparty = project.Counterparty; ContractType = project.ContractType; Notes = project.Notes; FolderName = item.FolderName;
            loadingSelection = true;
            try
            {
                if (project.BoundTemplateId is { } id && !Templates.Any(t => t.TemplateId == id) && templateService is not null) Templates.Add((await templateService.GetAsync(id)).Template);
                BoundTemplate = Templates.SingleOrDefault(t => t.TemplateId == project.BoundTemplateId); boundVersionId = project.BoundTemplateVersionId;
            }
            finally { loadingSelection = false; }
            Tags.Clear(); foreach (var tag in project.Tags) Tags.Add(tag); Message = "编辑项目不会改变已有版本或历史比对。";
            await Versions.OpenProjectAsync(project.ProjectId);
        });
    }
    [RelayCommand] private async Task NewProjectAsync()
    {
        if (IsBusy || Versions.IsBusy) return; editingProjectId = null; SelectedProject = null; BoundTemplate = null; boundVersionId = null; ProjectName = ""; Counterparty = ""; ContractType = ""; FolderName = ""; Notes = ""; Tags.Clear();
        Message = "填写项目名称，明确对方信息；绑定模板可继承合同类型。";
        await Versions.OpenProjectAsync(null);
    }
    [RelayCommand] private void SuggestName()
    { if (IsBusy) return; ProjectName = string.Join(" - ", new[] { BoundTemplate?.Name, Counterparty }.Where(x => !string.IsNullOrWhiteSpace(x))); Message = "这是轻量名称建议，请核对或修改后保存；对方名称不会自动猜测。"; }
    [RelayCommand] private void UnbindTemplate() { if (!IsBusy) { BoundTemplate = null; boundVersionId = null; } }
    [RelayCommand] private void ClearFilters()
    { SearchText = ""; FilterType = ""; FilterTemplate = null; FilterFolder = null; FilterTag = ""; TimeFilter = "全部时间"; }
    [RelayCommand] private void AddTag()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(TagName) || Tags.Any(t => t.Name.Equals(TagName.Trim(), StringComparison.OrdinalIgnoreCase))) { Message = "填写不重复的标签名称。"; return; }
        if (TagColor.Length != 7 || TagColor[0] != '#' || !TagColor[1..].All(Uri.IsHexDigit)) { Message = "颜色使用 #RRGGBB，或选择预设颜色。"; return; }
        Tags.Add(new(TagName.Trim(), TagColor.ToUpperInvariant())); TagName = "";
    }
    [RelayCommand] private void RemoveTag(ProjectTag? tag) { if (!IsBusy && tag is not null) Tags.Remove(tag); }
    [RelayCommand] private Task RefreshAsync() => PerformAsync(async () =>
    {
        if (templateService is not null)
        {
            var chosen = BoundTemplate; var filter = FilterTemplate; var templates = await templateService.ListAsync(); loadingSelection = true;
            try
            {
                Templates.Clear(); foreach (var template in templates) Templates.Add(template);
                // Preserve an existing disabled/deleted binding explicitly, never silently unbind it.
                if (chosen is not null && !Templates.Any(t => t.TemplateId == chosen.TemplateId)) Templates.Add((await templateService.GetAsync(chosen.TemplateId)).Template);
                BoundTemplate = Templates.SingleOrDefault(t => t.TemplateId == chosen?.TemplateId); FilterTemplate = Templates.SingleOrDefault(t => t.TemplateId == filter?.TemplateId);
            }
            finally { loadingSelection = false; }
        }
        await LoadCoreAsync(false);
    });
    [RelayCommand] private Task SearchAsync() => PerformAsync(() => LoadCoreAsync(false));
    [RelayCommand] private Task LoadMoreAsync() => PerformAsync(() => LoadCoreAsync(true));
    private async Task LoadCoreAsync(bool append)
    {
        if (service is null) return;
        var since = TimeFilter switch { "最近 7 天" => DateTimeOffset.UtcNow.AddDays(-7), "最近 30 天" => DateTimeOffset.UtcNow.AddDays(-30), _ => (DateTimeOffset?)null };
        if (!append) activeQuery = new(SearchText.Trim(), FilterTemplate?.TemplateId, FilterType.Trim(), since, FolderId: FilterFolder?.FolderId, Tag: FilterTag.Trim());
        var rows = await service.ListAsync(activeQuery with { Offset = append ? Projects.Count : 0 });
        var selectedId = editingProjectId;
        if (!append) Projects.Clear(); foreach (var row in rows) Projects.Add(row); HasMore = rows.Count == 20;
        SelectedProject = Projects.SingleOrDefault(x => x.Project.ProjectId == selectedId);
        var previousFolder = FilterFolder;
        Folders.Clear(); foreach (var folder in await service.FoldersAsync()) Folders.Add(folder);
        FilterFolder = Folders.SingleOrDefault(x => x.FolderId == previousFolder?.FolderId);
        Message = Projects.Count == 0 ? "尚无符合条件的合同项目。可新建项目或清除筛选。" : $"显示 {Projects.Count} 个项目，按最近更新排序。";
    }
    [RelayCommand] private Task SaveAsync() => PerformAsync(async () =>
    {
        if (service is null) return;
        Guid? templateId = BoundTemplate?.TemplateId;
        var versionId = boundVersionId;
        if (templateId is { } id && versionId is null && templateService is not null) versionId = (await templateService.GetAsync(id)).Current?.TemplateVersionId;
        var saved = await service.SaveAsync(editingProjectId, new(ProjectName, Counterparty, ContractType, templateId, versionId, FolderName, Tags.ToArray(), Notes));
        editingProjectId = saved.ProjectId;
        boundVersionId = saved.BoundTemplateVersionId; await LoadCoreAsync(false);
        SelectedProject = Projects.SingleOrDefault(x => x.Project.ProjectId == saved.ProjectId) ?? new(saved, BoundTemplate?.Name ?? "未绑定模板", "", FolderName);
        ContractType = saved.ContractType; Message = "项目已保存；模板切换仅影响未来比对，既有历史不改变。";
        if (Versions.ProjectId != saved.ProjectId) await Versions.OpenProjectAsync(saved.ProjectId);
    });
    private async Task PerformAsync(Func<Task> operation)
    {
        if (IsBusy || Versions.IsBusy) return; IsBusy = true;
        try { await operation(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException) { Message = e.Message; }
        catch (Exception) { Message = "项目操作失败，原有数据保持不变，请重试。"; }
        finally { IsBusy = false; }
    }
}
