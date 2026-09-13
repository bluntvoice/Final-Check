using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinalCheck.Core.Management;

namespace FinalCheck.App.ViewModels;

public partial class TemplateCenterViewModel(ITemplateService? service = null) : ViewModelBase
{
    public ObservableCollection<Template> Templates { get; } = [];
    public ObservableCollection<TemplateVersion> Versions { get; } = [];
    [ObservableProperty] private Template? selectedTemplate;
    [ObservableProperty] private TemplateVersion? selectedVersion;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string contractType = "";
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string version = "1.0.0";
    [ObservableProperty] private string sourcePath = "";
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string message = "尚未添加标准模板。选择或拖入 DOCX 后填写名称。";
    [ObservableProperty] private bool deletePrompt;
    [ObservableProperty] private IReadOnlyList<PreviewBlock> previewBlocks = [];
    public bool CanEdit => !IsBusy;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanEdit));
    partial void OnSelectedTemplateChanged(Template? value) { SelectedVersion = null; DeletePrompt = false; PreviewBlocks = []; }
    partial void OnSelectedVersionChanged(TemplateVersion? value) => PreviewBlocks = [];
    public async Task SelectTemplateAsync(Template? selected)
    {
        if (IsBusy || service is null || selected is null) return;
        await PerformAsync(async () =>
        {
            var detail = await service.GetAsync(selected.TemplateId); SelectedTemplate = detail.Template;
            Name = detail.Template.Name; ContractType = detail.Template.ContractType; Notes = detail.Template.Notes; Enabled = detail.Template.IsEnabled;
            Versions.Clear(); foreach (var item in detail.Versions) Versions.Add(item); SelectedVersion = detail.Current ?? (detail.Versions.Count > 0 ? detail.Versions[0] : null);
            Version = SuggestVersion(detail.Versions); SourcePath = ""; Message = "新版本保留历史；导入后可手动设为当前版本。";
        });
    }
    public Task AcceptFilesAsync(IEnumerable<string> paths)
    {
        if (IsBusy) return Task.CompletedTask;
        var items = paths.ToArray();
        if (items.Length != 1 || !Path.GetExtension(items[0]).Equals(".docx", StringComparison.OrdinalIgnoreCase)) Message = "每次请选择或拖入一个 DOCX。";
        else { SourcePath = items[0]; if (SelectedTemplate is null && string.IsNullOrWhiteSpace(Name)) Name = Path.GetFileNameWithoutExtension(items[0]); Message = "请核对模板名称与版本后导入。"; }
        return Task.CompletedTask;
    }
    [RelayCommand] private void NewTemplate()
    { if (IsBusy) return; SelectedTemplate = null; SelectedVersion = null; Versions.Clear(); Name = ""; ContractType = ""; Notes = ""; SourcePath = ""; Version = "1.0.0"; Enabled = true; Message = "选择或拖入 DOCX，填写新模板信息。"; }
    [RelayCommand] private Task RefreshAsync() => PerformAsync(RefreshCoreAsync);
    private async Task RefreshCoreAsync()
    {
        if (service is null) return;
        var rows = await service.ListAsync(); Templates.Clear(); foreach (var item in rows) Templates.Add(item);
        if (rows.Count == 0) Message = "尚未添加标准模板。";
    }
    [RelayCommand] private Task ImportAsync() => PerformAsync(async () =>
    {
        if (service is null || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(SourcePath))
        { Message = "请填写名称、版本并选择 DOCX。"; return; }
        var detail = await service.ImportAsync(SelectedTemplate?.TemplateId, Name, ContractType, Version, SourcePath);
        await RefreshCoreAsync(); SelectedTemplate = detail.Template; Versions.Clear(); foreach (var item in detail.Versions) Versions.Add(item);
        SelectedVersion = detail.Versions.Count > 0 ? detail.Versions[0] : null; Version = SuggestVersion(detail.Versions); SourcePath = "";
        Message = detail.Versions.Any(v => v.ParseStatus != Core.Documents.DocumentParseStatus.Complete) ? "模板已保存，但存在部分解析内容；查看和比对时需人工核验。" : "模板及快照已保存，原始 DOCX 保持不变。";
    });
    [RelayCommand] private Task SaveAsync() => PerformAsync(async () =>
    { if (service is null || SelectedTemplate is null) return; await service.UpdateAsync(SelectedTemplate.TemplateId, Name, ContractType, Notes, Enabled); await RefreshCoreAsync(); Message = "模板信息已保存。"; });
    [RelayCommand] private Task SetCurrentAsync() => PerformAsync(async () =>
    {
        if (service is null || SelectedTemplate is null || SelectedVersion is null) return;
        await service.SetCurrentAsync(SelectedTemplate.TemplateId, SelectedVersion.TemplateVersionId);
        var detail = await service.GetAsync(SelectedTemplate.TemplateId); Versions.Clear(); foreach (var v in detail.Versions) Versions.Add(v); SelectedVersion = detail.Current; Message = "当前版本已切换，历史版本与比对保持不变。";
    });
    [RelayCommand] private Task RelinkAsync() => PerformAsync(async () =>
    {
        if (service is null || SelectedVersion is null || string.IsNullOrWhiteSpace(SourcePath)) { Message = "选择历史版本，再选择内容一致的 DOCX 进行关联。"; return; }
        var id = SelectedVersion.TemplateVersionId; await service.RelinkAsync(id, SourcePath);
        var detail = await service.GetAsync(SelectedVersion.TemplateId); Versions.Clear(); foreach (var v in detail.Versions) Versions.Add(v); SelectedVersion = detail.Versions.Single(v => v.TemplateVersionId == id); Message = "源路径已更新，历史快照未改变。";
    });
    [RelayCommand] private Task FindSourceAsync() => PerformAsync(async () =>
    {
        if (service is null || SelectedVersion is null) return;
        var candidates = await service.FindRelinkCandidatesAsync(SelectedVersion);
        Message = candidates.Count == 0 ? "附近未找到内容一致的 DOCX，请手动选择。历史快照仍保留。" : $"找到 {candidates.Count} 个 hash 一致候选；请核对路径并点击重新关联。";
        if (candidates.Count > 0) SourcePath = candidates[0].Path;
    });
    [RelayCommand] private Task PreviewAsync() => PerformAsync(async () =>
    {
        if (service is null || SelectedVersion is null) return;
        var snapshot = await service.LoadSnapshotAsync(SelectedVersion.TemplateVersionId);
        PreviewBlocks = await Task.Run(() => ComparisonPreviewBuilder.Build(snapshot));
        Message = snapshot.ParseStatus == Core.Documents.DocumentParseStatus.Complete ? "已加载冻结历史快照；不访问原 DOCX，不保证 Word 高保真排版。" : "历史快照为部分解析，预览可能不完整。";
    });
    [RelayCommand] private Task RequestDeleteAsync() => PerformAsync(async () =>
    {
        if (service is null || SelectedTemplate is null) return;
        var references = await service.ReferencesAsync(SelectedTemplate.TemplateId);
        Message = $"删除模板将停止用于新比对。引用项目：{string.Join("、", references.Projects)}；历史比对：{references.Comparisons}。历史版本、快照和结果保留，外部 DOCX 不删除。"; DeletePrompt = true;
    });
    [RelayCommand] private void CancelDelete() => DeletePrompt = false;
    [RelayCommand] private Task ConfirmDeleteAsync() => PerformAsync(async () =>
    { if (service is null || SelectedTemplate is null || !DeletePrompt) return; await service.DeleteAsync(SelectedTemplate.TemplateId, true); DeletePrompt = false; SelectedTemplate = null; SelectedVersion = null; Versions.Clear(); await RefreshCoreAsync(); Message = "模板已移出模板中心；历史与原始文件保留。"; });
    private async Task PerformAsync(Func<Task> operation)
    {
        if (IsBusy) return; IsBusy = true;
        try { await operation(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException) { Message = e.Message; }
        catch (Exception) { Message = "操作失败，已有数据保持不变，请重试。"; }
        finally { IsBusy = false; }
    }
    public static string SuggestVersion(IEnumerable<TemplateVersion> versions)
    {
        var highest = versions.Select(v => System.Version.TryParse(v.Version.TrimStart('v'), out var parsed) ? parsed : null).OfType<System.Version>().OrderDescending().FirstOrDefault();
        return highest is null ? "1.0.0" : $"{highest.Major}.{highest.Minor}.{Math.Max(highest.Build, 0) + 1}";
    }
}
