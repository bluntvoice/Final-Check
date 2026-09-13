using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using FinalCheck.Core.Comparisons;

namespace FinalCheck.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel() : this(new ComparisonSetupViewModel()) { }
    private readonly IComparisonWorkflowService? workflow;
    public MainViewModel(ComparisonSetupViewModel comparison, IComparisonWorkflowService? workflow = null, TemplateCenterViewModel? templates = null)
    {
        Comparison = comparison; this.workflow = workflow; Templates = templates ?? new();
        Comparison.Completed += async result => { if (IsSetup) await ShowResultAsync(result); };
    }
    public ComparisonSetupViewModel Comparison { get; }
    public TemplateCenterViewModel Templates { get; }
    public bool IsTemplates => SelectedPage == "templates";
    [ObservableProperty] private ComparisonResultsViewModel? results;
    public bool HasResults => Results is not null;
    partial void OnResultsChanged(ComparisonResultsViewModel? value) => OnPropertyChanged(nameof(HasResults));
    private string? pendingPage;
    private int navigationRevision;
    [ObservableProperty] private bool leavePrompt;
    public bool IsHome => SelectedPage == "home";
    public bool IsSetup => SelectedPage == "compare";
    public bool IsResults => SelectedPage == "results";
    public bool IsOther => !IsHome && !IsSetup && !IsResults && !IsTemplates;
    partial void OnSelectedPageChanged(string value)
    { navigationRevision++; OnPropertyChanged(nameof(IsHome)); OnPropertyChanged(nameof(IsSetup)); OnPropertyChanged(nameof(IsOther)); OnPropertyChanged(nameof(IsResults)); OnPropertyChanged(nameof(IsTemplates)); }
    public string AppVersion { get; } = GetAppVersion();

    public string AppVersionLabel => $"v{AppVersion}";

    [ObservableProperty]
    private string currentPageTitle = "Final Check";

    [ObservableProperty]
    private string currentPageDescription = "选择两份 DOCX，查看文字、格式、修订与批注变化。";

    [ObservableProperty]
    private string selectedPage = "home";

    [RelayCommand]
    private void Navigate(string? page)
    {
        if (Comparison.IsExecuting) { pendingPage = page ?? "home"; LeavePrompt = true; return; }
        SelectedPage = page ?? "home";
        (CurrentPageTitle, CurrentPageDescription) = SelectedPage switch
        {
            "compare" => ("新建比对", "基准版本与当前版本 · 独立比对"),
            "results" => ("比对结果", "查看修改事实 · 原始文件保持不变"),
            "about" => ("关于 Final Check", $".NET 10 + Avalonia 12 · {AppVersionLabel}"),
            "templates" => ("模板中心", "维护标准模板及历史版本 · 原始 DOCX 保持不变"),
            _ => ("Final Check", "选择两份 DOCX，查看文字、格式、修订与批注变化。"),
        };
        if (IsTemplates) Templates.RefreshCommand.Execute(null);
    }
    private async Task ShowResultAsync(ComparisonWorkflowResult result)
    {
        var sourceRevision = navigationRevision;
        try
        {
            CurrentPageDescription = "正在准备文档预览…";
            var prepared = await ComparisonResultsViewModel.CreateAsync(result, workflow);
            if (navigationRevision != sourceRevision) return;
            Results = prepared; SelectedPage = "results";
            CurrentPageTitle = "比对结果"; CurrentPageDescription = "查看修改事实 · 原始文件保持不变";
        }
        catch (Exception) { CurrentPageDescription = "预览加载失败，已保存比对仍可从首页重试查看。"; }
    }
    [RelayCommand] private async Task LoadRecentAsync()
    {
        if (workflow is null || Comparison.IsExecuting) return;
        var sourceRevision = navigationRevision;
        try
        {
            CurrentPageDescription = "正在读取最近比对…";
            var records = await workflow.ListAsync();
            var recent = records.Count > 0 ? records[0] : null;
            if (recent is null) { CurrentPageDescription = "暂无比对记录，请新建比对。"; return; }
            var result = await workflow.LoadAsync(recent.RecordId);
            if (result is not null && navigationRevision == sourceRevision) await ShowResultAsync(result);
        }
        catch (Exception) { CurrentPageDescription = "无法读取历史，请重试。原数据未被修改。"; }
    }
    [RelayCommand] private void Stay() { LeavePrompt = false; pendingPage = null; }
    [RelayCommand] private void ConfirmLeave()
    {
        Comparison.CancelCommand.Execute(null); LeavePrompt = false;
        var destination = pendingPage ?? "home"; pendingPage = null;
        SelectedPage = destination;
        (CurrentPageTitle, CurrentPageDescription) = destination switch
        { "about" => ("关于 Final Check", $".NET 10 + Avalonia 12 · {AppVersionLabel}"), "templates" => ("模板中心", "维护标准模板及历史版本 · 原始 DOCX 保持不变"), _ => ("Final Check", "比对已请求取消。") };
        if (IsTemplates) Templates.RefreshCommand.Execute(null);
    }

    private static string GetAppVersion()
    {
        var assembly = typeof(MainViewModel).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "unknown";
    }
}
