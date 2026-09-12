using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.App.ViewModels;

public sealed partial class ComparisonSetupViewModel(IComparisonFileInspector? inspector = null, IComparisonWorkflowService? workflow = null) : ViewModelBase, IDisposable
{
    private readonly IComparisonFileInspector? inspector = inspector;
    private readonly IComparisonWorkflowService? workflow = workflow;
    private CancellationTokenSource? cancellation;
    private ComparisonInputValidation? pendingInput;
    private bool disposed;
    public event Action<ComparisonWorkflowResult>? Completed;
    public ComparisonSession Session { get; private set; } = new();
    [ObservableProperty] private ComparisonFile? baselineFile;
    [ObservableProperty] private ComparisonFile? currentFile;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string message = "请选择基准版本和当前版本，原始文件不会被修改。";
    [ObservableProperty] private bool identicalPrompt;
    [ObservableProperty] private bool isExecuting;
    [ObservableProperty] private string technicalDetails = "";
    [ObservableProperty] private ComparisonWorkflowResult? outcome;
    public bool CanStart => !disposed && BaselineFile is not null && CurrentFile is not null && !IsBusy && !IdenticalPrompt;
    public string BaselineInfo => FileInfoText(BaselineFile);
    public string CurrentInfo => FileInfoText(CurrentFile);
    partial void OnBaselineFileChanged(ComparisonFile? value) { Session.BaselineFile = value; Refresh(); }
    partial void OnCurrentFileChanged(ComparisonFile? value) { Session.CurrentFile = value; Refresh(); }
    partial void OnIsBusyChanged(bool value) => Refresh();
    partial void OnIdenticalPromptChanged(bool value) => Refresh();
    private void Refresh()
    {
        OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(BaselineInfo)); OnPropertyChanged(nameof(CurrentInfo));
        StartCommand.NotifyCanExecuteChanged();
    }
    private static string FileInfoText(ComparisonFile? file) => file is null ? "拖入一个 DOCX，或点击选择" :
        $"{file.Name}\n{file.Path}\n{file.Size:N0} 字节 · {file.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}\nSHA-256：已校验";

    public async Task SelectFilesAsync(bool baseline, IEnumerable<string> paths)
    {
        if (IsBusy || disposed || IdenticalPrompt) return;
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
    [RelayCommand] private void RemoveBaseline() { if (!IsBusy && !IdenticalPrompt) BaselineFile = null; }
    [RelayCommand] private void RemoveCurrent() { if (!IsBusy && !IdenticalPrompt) CurrentFile = null; }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (!CanStart || workflow is null) return;
        pendingInput = null; Outcome = null; TechnicalDetails = "";
        Session = new() { BaselineFile = BaselineFile, CurrentFile = CurrentFile };
        Session.Status = ComparisonSessionStatus.Validating; Session.StartedAt = DateTimeOffset.UtcNow;
        cancellation = new(); IsBusy = true; IsExecuting = true; Message = "正在校验文件…";
        try
        {
            var input = await workflow.ValidateAsync(BaselineFile!, CurrentFile!, cancellation.Token);
            BaselineFile = input.Baseline; CurrentFile = input.Current;
            if (input.SameHash)
            {
                pendingInput = input; IdenticalPrompt = true;
                Message = input.SamePath ? "两个路径指向同一个文件，内容完全一致。仍然比对将得到 0 项实际差异。" : "两个文件内容完全一致。是否仍然执行比对？";
                return;
            }
            await ExecuteAsync(input);
        }
        catch (Exception error) { HandleError(error); }
        finally { IsBusy = false; IsExecuting = false; cancellation?.Dispose(); cancellation = null; }
    }
    [RelayCommand] private async Task ContinueIdenticalAsync()
    {
        if (!IdenticalPrompt || pendingInput is null || IsBusy || workflow is null) return;
        var input = pendingInput; pendingInput = null; IdenticalPrompt = false;
        cancellation = new(); IsBusy = true; IsExecuting = true;
        try { await ExecuteAsync(input); }
        catch (Exception error) { HandleError(error); }
        finally { IsBusy = false; IsExecuting = false; cancellation.Dispose(); cancellation = null; }
    }
    private async Task ExecuteAsync(ComparisonInputValidation input)
    {
        var token = cancellation!.Token;
        var progress = new Progress<string>(value => { if (IsExecuting && Session.Status == ComparisonSessionStatus.Comparing && !token.IsCancellationRequested) Message = value; });
        Session.Status = ComparisonSessionStatus.Comparing;
        var result = await workflow!.ExecuteAsync(input, progress, token);
        Outcome = result; Session.Status = ComparisonSessionStatus.Completed; Session.CompletedAt = DateTimeOffset.UtcNow;
        Session.ResultId = result.Record.ResultId; Session.ParseStatus = result.IsPartial ? "部分解析" : "完整解析";
        Session.Diagnostics = result.Baseline.ParseDiagnostics.Concat(result.Current.ParseDiagnostics).Select(d => $"{d.Code} · {d.NodeId ?? d.SourcePart}").ToArray();
        OnPropertyChanged(nameof(Session));
        Message = result.IsPartial ? "本次比对结果可能不完整。请查看解析提示后决定是否继续。" : $"比对完成，共 {result.Result.Statistics.TotalChanges} 项修改。";
        if (!result.IsPartial) Completed?.Invoke(result);
    }
    [RelayCommand] private void ViewPartial() { if (Outcome is not null) Completed?.Invoke(Outcome); }
    [RelayCommand] private void Cancel()
    {
        cancellation?.Cancel(); pendingInput = null; IdenticalPrompt = false;
        if (!IsExecuting) { Session.Status = ComparisonSessionStatus.Cancelled; Message = "已取消，可以重新开始。"; }
    }
    private void HandleError(Exception error)
    {
        Session.Status = error is OperationCanceledException ? ComparisonSessionStatus.Cancelled : ComparisonSessionStatus.Failed;
        Message = error switch
        {
            OperationCanceledException => "已取消，可以重新开始。",
            FileNotFoundException or DirectoryNotFoundException => "文件已移动或删除，请重新选择。",
            UnauthorizedAccessException => "没有权限读取文件，请选择可读取的本地文件。",
            DocumentParseException { ErrorKind: DocumentParseErrorKind.Encrypted } => "文档已加密，无法读取。请先在 Word 中解除密码加密后重试。",
            DocumentParseException => "文件不是有效的 DOCX，或文档已损坏。请使用 Word 另存为 DOCX 后重试。",
            IOException => "无法读取或文件已发生变化，请关闭编辑器、刷新文件后重试。",
            _ => "比对未完成，请重试。若仍失败，请查看技术详情。",
        };
        Session.Error = Message;
        TechnicalDetails = error is DocumentParseException parse ? $"{error.GetType().Name} / {parse.ErrorKind}" : error.GetType().Name;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        cancellation?.Cancel();
        if (!IsExecuting) { cancellation?.Dispose(); cancellation = null; }
        Refresh();
    }
}
