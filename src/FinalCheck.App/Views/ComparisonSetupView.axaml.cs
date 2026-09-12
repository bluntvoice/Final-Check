using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

/// <summary>Platform input adapter only; validation and session state live outside the View.</summary>
public partial class ComparisonSetupView : UserControl
{
    public ComparisonSetupView()
    {
        InitializeComponent();
        AttachDrop(BaselineZone, true); AttachDrop(CurrentZone, false);
    }
    private void AttachDrop(Control zone, bool baseline)
    {
        DragDrop.SetAllowDrop(zone, true);
        DragDrop.AddDragOverHandler(zone, (_, e) =>
        { e.DragEffects = DataContext is ComparisonSetupViewModel { IsBusy: false } ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        DragDrop.AddDropHandler(zone, async (_, e) =>
        {
            e.Handled = true;
            if (DataContext is ComparisonSetupViewModel vm)
                await vm.SelectFilesAsync(baseline, (e.DataTransfer.TryGetFiles() ?? []).Select(f => f.TryGetLocalPath()).OfType<string>());
        });
    }
    private async void PickBaseline(object? sender, RoutedEventArgs e) => await PickAsync(true);
    private async void PickCurrent(object? sender, RoutedEventArgs e) => await PickAsync(false);
    private async Task PickAsync(bool baseline)
    {
        if (DataContext is not ComparisonSetupViewModel { IsBusy: false } vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = baseline ? "选择基准版本" : "选择当前版本", AllowMultiple = false,
              FileTypeFilter = [new FilePickerFileType("Word 文档") { Patterns = ["*.docx"] }] });
            if (files.Count > 0) await vm.SelectFilesAsync(baseline, files.Select(f => f.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { vm.Message = "无法打开文件选择器，请尝试拖入 DOCX。"; }
    }
}
