using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinalCheck.Core.Comparisons;

namespace FinalCheck.App.ViewModels;

public partial class ComparisonSetupViewModel(IComparisonFileInspector? inspector = null) : ViewModelBase
{
    private readonly IComparisonFileInspector? inspector = inspector;
    public ComparisonSession Session { get; private set; } = new();
    [ObservableProperty] private ComparisonFile? baselineFile;
    [ObservableProperty] private ComparisonFile? currentFile;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string message = "请选择基准版本和当前版本，原始文件不会被修改。";
    public bool CanStart => BaselineFile is not null && CurrentFile is not null && !IsBusy;
    public string BaselineInfo => FileInfoText(BaselineFile);
    public string CurrentInfo => FileInfoText(CurrentFile);
    partial void OnBaselineFileChanged(ComparisonFile? value) { Session.BaselineFile = value; Refresh(); }
    partial void OnCurrentFileChanged(ComparisonFile? value) { Session.CurrentFile = value; Refresh(); }
    partial void OnIsBusyChanged(bool value) => Refresh();
    private void Refresh()
    {
        OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(BaselineInfo)); OnPropertyChanged(nameof(CurrentInfo));
    }
    private static string FileInfoText(ComparisonFile? file) => file is null ? "拖入一个 DOCX，或点击选择" :
        $"{file.Name}\n{file.Path}\n{file.Size:N0} 字节 · {file.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}\nSHA-256：已校验";

    public async Task SelectFilesAsync(bool baseline, IEnumerable<string> paths)
    {
        if (IsBusy) return;
        var selected = paths.Take(2).ToArray();
        if (selected.Length != 1) { Message = "每侧请只选择一个 DOCX 文件。"; return; }
        if (inspector is null) { Message = "文件服务不可用。"; return; }
        IsBusy = true; Message = "正在校验文件…";
        try
        {
            var file = await inspector.InspectAsync(selected[0]);
            if (baseline) BaselineFile = file; else CurrentFile = file;
            Message = "文件已就绪。选择两份文件后可开始比对。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { Message = error is UnauthorizedAccessException ? "没有权限读取文件。" : error is FileNotFoundException ? "文件已移动或删除。" : error is ArgumentException ? "只支持 DOCX 文件。" : "无法读取文件，请关闭编辑器后重试。"; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void RemoveBaseline() { if (!IsBusy) BaselineFile = null; }
    [RelayCommand] private void RemoveCurrent() { if (!IsBusy) CurrentFile = null; }
}
