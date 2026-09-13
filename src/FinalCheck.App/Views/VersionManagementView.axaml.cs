using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;
public partial class VersionManagementView : UserControl
{
    public VersionManagementView()
    {
        InitializeComponent(); DragDrop.SetAllowDrop(DropZone, true);
        DragDrop.AddDragOverHandler(DropZone, (_, e) => { e.DragEffects = DataContext is VersionManagementViewModel { CanEdit: true } ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        DragDrop.AddDropHandler(DropZone, async (_, e) => { e.Handled = true; if (DataContext is VersionManagementViewModel vm) await vm.AcceptFilesAsync((e.DataTransfer.TryGetFiles() ?? []).Select(f => f.TryGetLocalPath()).OfType<string>()); });
    }
    private async void PickFiles(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VersionManagementViewModel { CanEdit: true } vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try { var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = "选择合同版本 DOCX", AllowMultiple = true, FileTypeFilter = [new("DOCX") { Patterns = ["*.docx"] }] }); if (files.Count > 0) await vm.AcceptFilesAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>()); }
        catch (Exception) { vm.Message = "文件选择器打开失败，请拖入 DOCX。"; }
    }
    private async void SelectVersion(object? sender, SelectionChangedEventArgs e)
    { if (DataContext is VersionManagementViewModel vm && e.AddedItems.OfType<VersionTimelineItem>().FirstOrDefault() is { } item) await vm.SelectVersionAsync(item); }
}
