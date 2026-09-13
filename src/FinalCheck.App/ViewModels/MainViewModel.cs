using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using FinalCheck.Core.Comparisons;

namespace FinalCheck.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel() : this(new ComparisonSetupViewModel()) { }
    private readonly IComparisonWorkflowService? workflow;
    public MainViewModel(ComparisonSetupViewModel comparison, IComparisonWorkflowService? workflow = null)
    {
        Comparison = comparison; this.workflow = workflow;
        Comparison.Completed += result => { if (IsSetup) ShowResult(result); };
    }
    public ComparisonSetupViewModel Comparison { get; }
    [ObservableProperty] private ComparisonResultsViewModel? results;
    public bool HasResults => Results is not null;
    partial void OnResultsChanged(ComparisonResultsViewModel? value) => OnPropertyChanged(nameof(HasResults));
    private string? pendingPage;
    [ObservableProperty] private bool leavePrompt;
    public bool IsHome => SelectedPage == "home";
    public bool IsSetup => SelectedPage == "compare";
    public bool IsResults => SelectedPage == "results";
    public bool IsOther => !IsHome && !IsSetup && !IsResults;
    partial void OnSelectedPageChanged(string value)
    { OnPropertyChanged(nameof(IsHome)); OnPropertyChanged(nameof(IsSetup)); OnPropertyChanged(nameof(IsOther)); OnPropertyChanged(nameof(IsResults)); }
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
            "templates" => ("模板中心", "模板管理功能尚未进入本轮实现范围。"),
            _ => ("Final Check", "选择两份 DOCX，查看文字、格式、修订与批注变化。"),
        };
    }
    private void ShowResult(ComparisonWorkflowResult result)
    {
        Results = new ComparisonResultsViewModel(result, workflow); SelectedPage = "results";
        CurrentPageTitle = "比对结果"; CurrentPageDescription = "查看修改事实 · 原始文件保持不变";
    }
    [RelayCommand] private async Task LoadRecentAsync()
    {
        if (workflow is null || Comparison.IsExecuting) return;
        try
        {
            CurrentPageDescription = "正在读取最近比对…";
            var records = await workflow.ListAsync();
            var recent = records.Count > 0 ? records[0] : null;
            if (recent is null) { CurrentPageDescription = "暂无比对记录，请新建比对。"; return; }
            var result = await workflow.LoadAsync(recent.RecordId);
            if (result is not null) ShowResult(result);
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
        { "about" => ("关于 Final Check", $".NET 10 + Avalonia 12 · {AppVersionLabel}"), "templates" => ("模板中心", "模板管理功能尚未进入本轮实现范围。"), _ => ("Final Check", "比对已请求取消。") };
    }

    private static string GetAppVersion()
    {
        var assembly = typeof(MainViewModel).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "unknown";
    }
}
