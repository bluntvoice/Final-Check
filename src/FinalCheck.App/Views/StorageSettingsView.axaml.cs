using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.ComponentModel;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

public partial class StorageSettingsView : UserControl
{
    private StorageSettingsViewModel? observed;
    public StorageSettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        AttachedToVisualTree += (_, _) => Observe();
        DetachedFromVisualTree += (_, _) => { if (observed is not null) observed.PropertyChanged -= ModelChanged; observed = null; };
    }
    private void Observe()
    {
        if (observed is not null) observed.PropertyChanged -= ModelChanged;
        observed = DataContext as StorageSettingsViewModel;
        if (observed is not null) observed.PropertyChanged += ModelChanged;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StorageSettingsViewModel.ShowConfirmation) && observed?.ShowConfirmation == true)
            Dispatcher.UIThread.Post(() => { if (observed?.ShowConfirmation == true) MigrationConfirmation.BringIntoView(); }, DispatcherPriority.Loaded);
        if (e.PropertyName == nameof(StorageSettingsViewModel.Message) && !string.IsNullOrWhiteSpace(observed?.Message))
            Dispatcher.UIThread.Post(() => MigrationMessage.BringIntoView(), DispatcherPriority.Loaded);
    }

    private async void PickFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StorageSettingsViewModel { CanEdit: true } model || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var selected = await top.StorageProvider.OpenFolderPickerAsync(new() { Title = "选择新的空数据文件夹", AllowMultiple = false });
            if (selected.Count > 0 && model.CanEdit && selected[0].TryGetLocalPath() is { } path) model.TargetPath = path;
        }
        catch (Exception) { model.Message = "文件夹选择器打开失败，可以在输入框填写完整数据路径。"; }
    }
}
