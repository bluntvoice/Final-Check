using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

public partial class TemplateCenterView : UserControl
{
    public TemplateCenterView()
    {
        InitializeComponent(); DragDrop.SetAllowDrop(DropZone, true);
        DragDrop.AddDragOverHandler(DropZone, (_, e) => { e.DragEffects = DataContext is TemplateCenterViewModel { IsBusy: false } ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        DragDrop.AddDropHandler(DropZone, async (_, e) => { e.Handled = true; if (DataContext is TemplateCenterViewModel vm) await vm.AcceptFilesAsync((e.DataTransfer.TryGetFiles() ?? []).Select(f => f.TryGetLocalPath()).OfType<string>()); });
    }
    private async void SelectTemplate(object? sender, SelectionChangedEventArgs e)
    { if (DataContext is TemplateCenterViewModel vm && e.AddedItems.OfType<Core.Management.Template>().FirstOrDefault() is { } selected) await vm.SelectTemplateAsync(selected); }
    private async void PickFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TemplateCenterViewModel { IsBusy: false } vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try { var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = "选择模板 DOCX", AllowMultiple = false, FileTypeFilter = [new("DOCX") { Patterns = ["*.docx"] }] }); if (files.Count > 0) await vm.AcceptFilesAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>()); }
        catch (Exception) { vm.Message = "选择器打开失败，请拖入 DOCX。"; }
    }
}
