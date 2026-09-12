using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;

namespace FinalCheck.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public string AppVersion { get; } = GetAppVersion();

    public string AppVersionLabel => $"v{AppVersion}";

    [ObservableProperty]
    private string currentPageTitle = "项目";

    [ObservableProperty]
    private string currentPageDescription = "合同项目将在后续 Document Engine 阶段接入。";

    [ObservableProperty]
    private string selectedPage = "home";

    [RelayCommand]
    private void Navigate(string? page)
    {
        SelectedPage = page ?? "home";
        (CurrentPageTitle, CurrentPageDescription) = SelectedPage switch
        {
            "about" => ("关于 Final Check", $".NET 10 + Avalonia 12 · {AppVersionLabel}"),
            "templates" => ("模板中心", "模板管理功能尚未进入本轮实现范围。"),
            _ => ("项目", "合同项目将在后续 Document Engine 阶段接入。"),
        };
    }

    private static string GetAppVersion()
    {
        var assembly = typeof(MainViewModel).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "unknown";
    }
}
