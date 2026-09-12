using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;

namespace FinalCheck.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel() : this(new ComparisonSetupViewModel()) { }
    public MainViewModel(ComparisonSetupViewModel comparison) { Comparison = comparison; }
    public ComparisonSetupViewModel Comparison { get; }
    private string? pendingPage;
    [ObservableProperty] private bool leavePrompt;
    public bool IsHome => SelectedPage == "home";
    public bool IsSetup => SelectedPage == "compare";
    public bool IsOther => !IsHome && !IsSetup;
    partial void OnSelectedPageChanged(string value)
    { OnPropertyChanged(nameof(IsHome)); OnPropertyChanged(nameof(IsSetup)); OnPropertyChanged(nameof(IsOther)); }
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
            "about" => ("关于 Final Check", $".NET 10 + Avalonia 12 · {AppVersionLabel}"),
            "templates" => ("模板中心", "模板管理功能尚未进入本轮实现范围。"),
            _ => ("Final Check", "选择两份 DOCX，查看文字、格式、修订与批注变化。"),
        };
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
