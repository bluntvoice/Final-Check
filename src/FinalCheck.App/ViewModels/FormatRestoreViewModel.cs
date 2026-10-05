using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinalCheck.Core.Formatting;

namespace FinalCheck.App.ViewModels;

public sealed record RestorePropertyPreview(string Name, string Current, string Target);

public sealed partial class RestoreItemViewModel : ViewModelBase
{
    private readonly Action changed;
    public RestoreItemViewModel(FormatRestoreItem item, IReadOnlyDictionary<string, string> locations, Action changed)
    {
        Item = item; this.changed = changed;
        Location = locations.GetValueOrDefault(item.CurrentNodeId, "位置暂无法定位") +
            (item.NodeType == Core.Documents.DocumentNodeKind.Run ? " / 文字片段" : "");
        using var current = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(item.CurrentFormatting));
        using var target = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(item.TargetFormatting));
        Properties = item.Difference.Select(path => new RestorePropertyPreview(PropertyLabel(path),
            PropertyValue(current.RootElement, path), PropertyValue(target.RootElement, path))).ToArray();
    }
    public FormatRestoreItem Item { get; }
    public bool CanSelect => Item.Eligibility == FormatRestoreEligibility.Eligible;
    private bool selected;
    public bool Selected
    {
        get => selected;
        set { if (SetProperty(ref selected, value && CanSelect)) changed(); }
    }
    public string Location { get; }
    public string CategoryLabel => CategoryName(Item.Category);
    public string EligibilityLabel => Item.Eligibility switch { FormatRestoreEligibility.Eligible => "可恢复", FormatRestoreEligibility.NeedsReview => "需审阅，自动恢复跳过", _ => "暂不支持" };
    public string ConfidenceLabel => $"映射可信度 {Item.Confidence.ToString("P0", CultureInfo.CurrentCulture)}";
    public IReadOnlyList<RestorePropertyPreview> Properties { get; }
    public string Diagnostics => string.Join("；", Item.Diagnostics.Select(DiagnosticText)) +
        (Item.FallbackSource is null ? "" : "；使用映射或邻近格式作为目标，请核对位置。");
    public static string CategoryName(FormatRestoreCategory category) => category switch
    { FormatRestoreCategory.Character => "字符", FormatRestoreCategory.Paragraph => "段落", FormatRestoreCategory.Table => "表格", _ => "单元格" };
    internal static string DiagnosticText(FormatRestoreDiagnostic diagnostic) => diagnostic.Code switch
    {
        "LowConfidenceMapping" => "映射证据不足，保留当前格式", "UnmappedNode" => "没有可靠对应位置，保留当前格式",
        "UnsupportedTableStyle" => "表格样式/条件格式暂不支持完整恢复", "MergeStructurePreserved" => "保留当前合并结构",
        "StyleReferencePreserved" => "目标样式不兼容，保留现有样式引用", "AddedParagraphFallbackUsed" => "新增段落使用两侧一致的邻近格式",
        "AddedTextFallbackUsed" => "新增文字使用可靠邻近目标格式", "ExistingFormatRevisionPreserved" => "保留原有格式修订",
        _ => "恢复提示（" + diagnostic.Code + "）",
    };
    private static string PropertyLabel(string path)
    {
        var property = path.Contains('.') ? path[(path.IndexOf('.') + 1)..] : path;
        property = property.Replace("Fonts.", "Font.", StringComparison.Ordinal);
        return property switch
        {
            "ParagraphStyleId" => "段落样式", "RowHeightTwips" => "行高（twip）", "RowHeightRule" => "行高规则", "WidthType" => "宽度单位",
            _ when property.StartsWith("Borders.", StringComparison.Ordinal) => "边框（" + property[8..] + "）",
            _ => ChangeItemViewModel.PropertyName(property),
        };
    }
    private static string PropertyValue(JsonElement root, string path)
    {
        var value = root;
        foreach (var segment in path.Split('.'))
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value)) return "未指定";
        if (value.ValueKind == JsonValueKind.Null) return "未指定";
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return ChangeItemViewModel.FormatValue(path[(path.LastIndexOf('.') + 1)..], text);
    }
}

public sealed partial class FormatRestoreViewModel(IFormatRestoreWorkspaceService? service, Guid recordId) : ViewModelBase
{
    public FormatRestoreAnalysis? Analysis { get; private set; }
    private bool changingSelection;
    public ObservableCollection<RestoreItemViewModel> Items { get; } = [];
    [ObservableProperty] private bool isAnalyzing;
    [ObservableProperty] private string message = "点击分析，预览当前版本的格式恢复项目。";
    [ObservableProperty] private string category = "全部";
    public IReadOnlyList<string> Categories { get; } = ["全部", "字符", "段落", "表格", "单元格"];
    public bool HasPlan => Analysis?.Plan is not null;
    public string SourceLabel => Analysis?.WorkingCopy is null ? "恢复来源：原始修订版" : "恢复来源：当前格式恢复版";
    public string SourcePath => Analysis?.SourcePath ?? "";
    public string Summary => !HasPlan ? "尚无可用计划" :
        $"共 {Items.Count} 项 · 可恢复 {Items.Count(item => item.CanSelect)} · 需审阅/不支持 {Items.Count(item => !item.CanSelect)} · 已选 {Items.Count(item => item.Selected)}";
    public string Diagnostics => string.Join("\n", Analysis?.Plan?.Diagnostics.Select(RestoreItemViewModel.DiagnosticText) ?? []);
    public bool CanSelect => HasPlan && !IsAnalyzing;
    public string SelectionSummary => string.Join("\n", Items.Where(item => item.Selected).Select(item => $"{item.CategoryLabel} · {item.Location}"));
    public FormatRestoreScope Selection => new(FormatRestoreScopeKind.SelectedItems, SelectedItemIds: Items.Where(item => item.Selected).Select(item => item.Item.RestoreItemId).ToArray());
    partial void OnIsAnalyzingChanged(bool value) => OnPropertyChanged(nameof(CanSelect));
    [RelayCommand] private async Task AnalyzeAsync()
    {
        Analysis = null; Items.Clear(); NotifyPlan();
        if (service is null) { Message = "恢复分析服务不可用。"; return; }
        IsAnalyzing = true; Message = "正在核验当前文件与历史映射…";
        try
        {
            Analysis = await service.AnalyzeAsync(recordId);
            if (Analysis.Plan is { } plan && Analysis.Comparison is { } comparison)
            {
                var locations = ChangeItemViewModel.Locations(comparison.Current);
                foreach (var item in plan.RestoreItems) Items.Add(new(item, locations, SelectionChanged));
            }
            Message = Analysis.Message;
            if (HasPlan && Items.Count == 0) Message += " 没有可列出的格式差异；请同时核对解析与恢复提示。";
        }
        catch (Exception) { Analysis = null; Message = "恢复计划加载失败，请重试或检查文件与存储状态。"; }
        finally { IsAnalyzing = false; NotifyPlan(); }
    }
    [RelayCommand] private void SelectAll() => Choose(item => item.CanSelect);
    [RelayCommand] private void SelectCategory()
    {
        Choose(item => item.CanSelect &&
            (Category == "全部" || item.CategoryLabel == Category || Category == "表格" && item.Item.Category == FormatRestoreCategory.Cell));
    }
    [RelayCommand] private void ClearSelection() => Choose(_ => false);
    private void Choose(Func<RestoreItemViewModel, bool> select)
    {
        if (!CanSelect) return;
        changingSelection = true;
        try { foreach (var item in Items) item.Selected = select(item); }
        finally { changingSelection = false; SelectionChanged(); }
    }
    private void SelectionChanged()
    {
        if (changingSelection) return;
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(SelectionSummary));
    }
    private void NotifyPlan()
    {
        OnPropertyChanged(nameof(HasPlan)); OnPropertyChanged(nameof(CanSelect)); OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SourceLabel)); OnPropertyChanged(nameof(SourcePath)); OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(SelectionSummary));
    }
}
