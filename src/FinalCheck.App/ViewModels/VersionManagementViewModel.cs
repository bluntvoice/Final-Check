using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Management;

namespace FinalCheck.App.ViewModels;

public partial class VersionImportRow(ComparisonFile source, int round) : ViewModelBase
{
    public ComparisonFile Source { get; } = source;
    public bool ExistingDuplicate { get; set; }
    public IReadOnlyList<string> Roles { get; } = ["我方版本", "对方版本"];
    [ObservableProperty] private string? role;
    [ObservableProperty] private int roundNumber = round;
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private bool duplicate;
    [ObservableProperty] private bool allowDuplicate;
    public string DuplicateMessage => Duplicate ? "已存在内容完全相同的版本：默认跳过，可勾选仍然导入。" : "";
    partial void OnDuplicateChanged(bool value) => OnPropertyChanged(nameof(DuplicateMessage));
}
public sealed record VersionTimelineItem(ContractVersion Version)
{
    public string Heading => $"第 {Version.RoundNumber} 轮 · {(Version.Role == ContractVersionRole.Own ? "我方" : "对方")} · {Version.Source.Name}";
    public string State => $"{Version.ParseStatus}{(Version.IsCurrentBaseline ? " · 当前我方基准" : "")}{(Version.DuplicateReference is null ? "" : " · 内容相同")}";
}
public partial class VersionManagementViewModel(IContractVersionService? service = null, IProjectComparisonService? comparisons = null, IComparisonWorkflowService? workflow = null) : ViewModelBase
{
    public event Func<ComparisonWorkflowResult, Task>? Completed;
    public ObservableCollection<ProjectBaselineOption> Baselines { get; } = [];
    public ObservableCollection<ContractVersion> NewOwnVersions { get; } = [];
    public ObservableCollection<ProjectComparisonHistory> History { get; } = [];
    [ObservableProperty] private ProjectBaselineOption? selectedBaseline;
    [ObservableProperty] private ContractVersion? newOwnBaseline;
    [ObservableProperty] private bool baselinePrompt;
    [ObservableProperty] private bool isComparing;
    [ObservableProperty] private string baselineRecommendation = "";
    private CancellationTokenSource? comparisonCancellation;
    public ObservableCollection<VersionImportRow> ImportQueue { get; } = [];
    public ObservableCollection<VersionTimelineItem> Versions { get; } = [];
    public ObservableCollection<int> RoundNumbers { get; } = [];
    public ObservableCollection<PreviewBlock> PreviewBlocks { get; } = [];
    public IReadOnlyList<string> OrderOptions { get; } = ["按轮次", "按时间顺序"];
    [ObservableProperty] private Guid? projectId;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private string order = "按轮次";
    [ObservableProperty] private int importRound = 1;
    [ObservableProperty] private VersionTimelineItem? selectedVersion;
    [ObservableProperty] private string sourcePath = "";
    [ObservableProperty] private string detail = "";
    [ObservableProperty] private string message = "导入第一份合同版本开始管理。";
    public bool CanEdit => ProjectId is not null && !IsBusy;
    private bool loadedChronological;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanEdit));
    partial void OnProjectIdChanged(Guid? value) => OnPropertyChanged(nameof(CanEdit));
    public async Task OpenProjectAsync(Guid? id)
    {
        if (IsBusy) return; ProjectId = id; ImportQueue.Clear(); Versions.Clear(); RoundNumbers.Clear(); SelectedVersion = null; PreviewBlocks.Clear(); Detail = ""; SourcePath = ""; Baselines.Clear(); History.Clear(); NewOwnVersions.Clear(); BaselinePrompt = false; SelectedBaseline = null;
        if (id is not null) await RefreshAsync();
    }
    public Task AcceptFilesAsync(IEnumerable<string> paths) => PerformAsync(async () =>
    {
        if (service is null || ProjectId is not { } id) return;
        var incoming = paths.ToArray();
        if (incoming.Length == 0 || incoming.Any(p => !p.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) || incoming.Length + ImportQueue.Count > 100) throw new ArgumentException("请选择 1–100 份 DOCX。");
        // No role guess or parse. Queue rows require explicit user selection before import.
        foreach (var path in incoming)
        {
            var file = await service.InspectAsync(path); var row = new VersionImportRow(file, ImportRound);
            row.ExistingDuplicate = (await service.SameContentAsync(id, file.Sha256)).Count > 0; ImportQueue.Add(row); RecheckQueueDuplicates();
        }
        Message = "请逐份明确选择我方/对方和轮次；重复内容默认跳过。导入不会自动比对。";
    });
    [RelayCommand] private void CurrentRound() { ImportRound = RoundNumbers.Count == 0 ? 1 : RoundNumbers.Max(); }
    [RelayCommand] private void NextRound() { ImportRound = RoundNumbers.Count == 0 ? 1 : RoundNumbers.Max() + 1; }
    [RelayCommand] private void ApplyRound() { if (!IsBusy) foreach (var row in ImportQueue) row.RoundNumber = ImportRound; }
    [RelayCommand] private void Remove(VersionImportRow? row) { if (!IsBusy && row is not null) { ImportQueue.Remove(row); RecheckQueueDuplicates(); } }
    [RelayCommand] private void MoveUp(VersionImportRow? row) { var index = row is null ? -1 : ImportQueue.IndexOf(row); if (!IsBusy && index > 0) { ImportQueue.Move(index, index - 1); RecheckQueueDuplicates(); } }
    [RelayCommand] private void MoveDown(VersionImportRow? row) { var index = row is null ? -1 : ImportQueue.IndexOf(row); if (!IsBusy && index >= 0 && index < ImportQueue.Count - 1) { ImportQueue.Move(index, index + 1); RecheckQueueDuplicates(); } }
    private void RecheckQueueDuplicates() { var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var row in ImportQueue) row.Duplicate = row.ExistingDuplicate || !hashes.Add(row.Source.Sha256); }
    [RelayCommand] private Task ImportAsync() => PerformAsync(async () =>
    {
        if (service is null || ProjectId is not { } id) return;
        if (ImportQueue.Any(x => x.Role is not ("我方版本" or "对方版本"))) throw new ArgumentException("每份文件都必须明确选择角色（包括准备跳过的重复项）。");
        var selected = ImportQueue.Where(x => !x.Duplicate || x.AllowDuplicate).ToArray();
        if (selected.Length == 0) { Message = "重复内容已跳过，没有新增版本。"; ImportQueue.Clear(); return; }
        var result = await service.ImportAsync(id, selected.Select(x => new VersionImport(x.Source.Path, x.Role == "我方版本" ? ContractVersionRole.Own : ContractVersionRole.Counterparty, x.RoundNumber, x.Notes, x.AllowDuplicate, x.Source.Sha256)).ToArray());
        ImportQueue.Clear(); await LoadCoreAsync(false); Message = $"已导入 {result.Count} 份版本；未自动比对或改变基准。";
        NewOwnVersions.Clear(); foreach (var own in result.Where(x => x.Role == ContractVersionRole.Own)) NewOwnVersions.Add(own); NewOwnBaseline = null; BaselinePrompt = comparisons is not null && NewOwnVersions.Count > 0;
    });
    [RelayCommand] private Task RefreshAsync() => PerformAsync(() => LoadCoreAsync(false));
    [RelayCommand] private Task LoadMoreAsync() => PerformAsync(() => LoadCoreAsync(true));
    private async Task LoadCoreAsync(bool append)
    {
        if (service is null || ProjectId is not { } id) return;
        if (!append) loadedChronological = Order == "按时间顺序";
        var versions = await service.ListAsync(id, loadedChronological, append ? Versions.Count : 0); if (!append) { SelectedVersion = null; SelectedBaseline = null; Baselines.Clear(); History.Clear(); Detail = ""; SourcePath = ""; PreviewBlocks.Clear(); Versions.Clear(); }
        foreach (var version in versions) Versions.Add(new(version)); HasMore = versions.Count == 20;
        RoundNumbers.Clear(); foreach (var round in await service.RoundsAsync(id)) RoundNumbers.Add(round.Number);
        ImportRound = RoundNumbers.Count == 0 ? 1 : RoundNumbers.Max();
        Message = Versions.Count == 0 ? "导入第一份合同版本开始管理。" : $"显示 {Versions.Count} 份版本；完整 Snapshot 仅在预览时读取。";
    }
    public Task SelectVersionAsync(VersionTimelineItem item) => PerformAsync(async () =>
    {
        if (service is null) return; var version = await service.GetAsync(item.Version.ContractVersionId); SelectedVersion = item; SourcePath = version.Source.Path; PreviewBlocks.Clear();
        Detail = $"{new VersionTimelineItem(version).Heading}\nSHA-256: {version.Source.Sha256}\n导入: {version.ImportedAt:u}\nSnapshot: {version.SnapshotId} · {version.ParseStatus}\n{version.Notes}";
        if (comparisons is not null && ProjectId is { } id)
        {
            var choices = await comparisons.ChoicesAsync(id, version.ContractVersionId); Baselines.Clear(); foreach (var option in choices.Options) Baselines.Add(option);
            SelectedBaseline = Baselines.FirstOrDefault(x => x.Type == choices.LastType && x.IsCurrent) ?? Baselines.FirstOrDefault(x => x.IsCurrent);
            BaselineRecommendation = choices.Recommendation + "记忆仅预选类型，仍需点击执行；比对已导入的冻结版本，不重新读取变化后的源文件。";
            History.Clear(); foreach (var history in await comparisons.HistoryAsync(id, version.ContractVersionId)) History.Add(history);
        }
    });
    [RelayCommand] private void KeepBaseline() { if (!IsBusy) { BaselinePrompt = false; NewOwnBaseline = null; Message = "保留原基准，新我方版本未设为基准。"; } }
    [RelayCommand] private Task ConfirmNewBaselineAsync() => PerformAsync(async () =>
    {
        if (comparisons is null || ProjectId is not { } id || NewOwnBaseline is not { } own || !NewOwnVersions.Any(x => x.ContractVersionId == own.ContractVersionId)) throw new ArgumentException("先明确选择本次导入的我方版本，默认不设置。");
        await comparisons.SetBaselineAsync(id, own.ContractVersionId); BaselinePrompt = false; await LoadCoreAsync(false); Message = "当前我方基准已明确更新，历史比对保持不变。";
    });
    [RelayCommand] private Task SetBaselineAsync() => PerformAsync(async () =>
    {
        if (comparisons is null || ProjectId is not { } id || SelectedVersion is not { } item) return;
        await comparisons.SetBaselineAsync(id, item.Version.ContractVersionId); await LoadCoreAsync(false); Message = "已设置当前我方基准，历史比对保持不变。";
    });
    [RelayCommand] private Task CompareAsync() => PerformAsync(async () =>
    {
        if (comparisons is null || ProjectId is not { } id || SelectedVersion is not { } item || SelectedBaseline is not { } baseline) throw new ArgumentException("请选择版本和明确基准后执行。");
        using var cancellation = new CancellationTokenSource(); comparisonCancellation = cancellation; IsComparing = true;
        try
        {
            var result = await comparisons.CompareAsync(new(id, item.Version.ContractVersionId, baseline.Type, baseline.VersionId), new Progress<string>(stage => Message = stage), cancellation.Token);
            Message = result.IsPartial ? "独立历史已保存；Partial / 低可信差异请人工审阅。" : "独立比对已保存；正在打开共用结果页。";
            if (Completed is { } handler) await handler(result);
        }
        catch (OperationCanceledException) { Message = "已取消未完成比对，既有历史保留。"; }
        finally { comparisonCancellation = null; IsComparing = false; }
    });
    [RelayCommand] private void CancelComparison() => comparisonCancellation?.Cancel();
    [RelayCommand] private Task OpenHistoryAsync(ProjectComparisonHistory? history) => PerformAsync(async () =>
    { if (workflow is null || history is null || history.ProjectId != ProjectId) return; var result = await workflow.LoadAsync(history.RecordId) ?? throw new InvalidDataException("历史记录缺失。"); if (Completed is { } handler) await handler(result); });
    [RelayCommand] private Task PreviewAsync() => PerformAsync(async () =>
    {
        if (service is null || SelectedVersion is not { } item) return; var snapshot = await service.LoadSnapshotAsync(item.Version.ContractVersionId);
        var blocks = await Task.Run(() => ComparisonPreviewBuilder.Build(snapshot)); PreviewBlocks.Clear(); foreach (var block in blocks) PreviewBlocks.Add(block);
        Message = "已加载冻结 Snapshot 预览，不依赖源文件仍存在；不是 Word 完整渲染。";
    });
    [RelayCommand] private Task FindSourceAsync() => PerformAsync(async () =>
    {
        if (service is null || SelectedVersion is not { } item) return; var matches = await service.FindRelinkCandidatesAsync(item.Version);
        if (matches.Count > 0) SourcePath = matches[0].Path; Message = matches.Count == 0 ? "附近未找到相同 hash，请手动选择路径。" : "找到相同 hash 候选，请核对后另行点击重新关联。";
    });
    [RelayCommand] private Task RelinkAsync() => PerformAsync(async () =>
    {
        if (service is null || SelectedVersion is not { } item) return; await service.RelinkAsync(item.Version.ContractVersionId, SourcePath); Message = "源路径已更新，历史 Snapshot 和原始来源 metadata 保留。";
    });
    private async Task PerformAsync(Func<Task> operation)
    {
        if (IsBusy) return; IsBusy = true;
        try { await operation(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException) { Message = e.Message; }
        catch (Exception) { Message = "版本操作失败，已保存历史保留，请重试。"; }
        finally { IsBusy = false; }
    }
}
