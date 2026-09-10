using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FinalCheck.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
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
            "about" => ("关于 Final Check", ".NET 10 + Avalonia 12 · v0.1.0 开发阶段"),
            "templates" => ("模板中心", "模板管理功能尚未进入本轮实现范围。"),
            _ => ("项目", "合同项目将在后续 Document Engine 阶段接入。"),
        };
    }
}
