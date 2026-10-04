using Avalonia.Controls;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (DataContext is MainViewModel { Storage.IsMigrating: true } model)
            {
                e.Cancel = true;
                model.Storage.Message = "数据位置正在更改，请等待完成或请求取消后再关闭。提交时会完成安全切换。";
            }
        };
    }
}
