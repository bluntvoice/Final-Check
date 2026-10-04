using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinalCheck.Core.Storage;

namespace FinalCheck.App.ViewModels;

public sealed record StorageUsageRow(string Name, string Size, string Description);

public partial class StorageSettingsViewModel : ViewModelBase
{
    private readonly IDataRootProvider? provider;
    private readonly IStorageUsageService? usageService;
    private readonly IDataRootValidator? validator;
    private readonly IDataRootMigrationService? migration;
    private readonly IStorageFolderOpener? folders;
    private DataRootDescriptor? confirmedSource;
    private string? confirmedTarget;

    public StorageSettingsViewModel() { }
    public StorageSettingsViewModel(IDataRootProvider provider, IStorageUsageService usageService,
        IDataRootValidator validator, IDataRootMigrationService migration, IStorageFolderOpener folders)
    {
        this.provider = provider; this.usageService = usageService; this.validator = validator;
        this.migration = migration; this.folders = folders;
        CurrentPath = provider.CurrentDataRoot;
    }

    public ObservableCollection<StorageUsageRow> PhysicalUsage { get; } = [];
    public ObservableCollection<StorageUsageRow> DatabasePayloads { get; } = [];
    [ObservableProperty] private string currentPath = "尚未读取";
    [ObservableProperty] private string totalSize = "尚未统计";
    [ObservableProperty] private string sampledAt = "";
    [ObservableProperty] private string usageMessage = "";
    [ObservableProperty] private string message = "";
    [ObservableProperty] private string targetPath = "";
    [ObservableProperty] private string confirmationSource = "";
    [ObservableProperty] private string confirmationTarget = "";
    [ObservableProperty] private string spaceMessage = "";
    [ObservableProperty] private bool showConfirmation;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isMigrating;
    [ObservableProperty] private bool recoveryRequired;
    public bool CanEdit => !IsBusy && !RecoveryRequired;
    public bool CanConfirm => CanEdit && ShowConfirmation;
    public bool CanCancel => IsMigrating && MigrateCommand.CanBeCanceled;
    public string BusyLabel => IsMigrating ? "正在复制、校验并切换数据位置…" : "正在读取和验证…";

    partial void OnTargetPathChanged(string value) => ClearConfirmation();
    partial void OnIsBusyChanged(bool value) => NotifyAvailability();
    partial void OnRecoveryRequiredChanged(bool value) => NotifyAvailability();
    partial void OnShowConfirmationChanged(bool value) => NotifyAvailability();
    partial void OnIsMigratingChanged(bool value)
    { OnPropertyChanged(nameof(BusyLabel)); OnPropertyChanged(nameof(CanCancel)); }
    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanConfirm)); OnPropertyChanged(nameof(CanCancel));
        RefreshCommand.NotifyCanExecuteChanged(); ValidateTargetCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged(); MigrateCommand.NotifyCanExecuteChanged();
    }
    private void ClearConfirmation()
    { ShowConfirmation = false; confirmedSource = null; confirmedTarget = null; SpaceMessage = ""; }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task RefreshAsync()
    {
        if (usageService is null) return;
        IsBusy = true;
        try { await ReadUsageAsync(CancellationToken.None); }
        finally { IsBusy = false; }
    }

    private async Task<StorageUsage?> ReadUsageAsync(CancellationToken token)
    {
        try
        {
            var usage = await Task.Run(() => usageService!.CalculateAsync(token), token);
            CurrentPath = usage.Root.Path;
            TotalSize = (usage.IsComplete ? "" : "已统计 ") + FormatBytes(usage.TotalBytes);
            SampledAt = $"统计时间：{usage.SampledAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            UsageMessage = usage.IsComplete ? "原始 DOCX 和手动导出文件不计入数据占用。"
                : "统计未完成，显示的是已读取部分。请检查目录权限后刷新。诊断：" + string.Join("、", usage.Diagnostics);
            PhysicalUsage.Clear(); DatabasePayloads.Clear();
            PhysicalUsage.Add(new("Database / 数据库", FormatBytes(usage.DatabaseBytes), "包括数据库、WAL 与共享内存文件"));
            PhysicalUsage.Add(new("Working Copy / 工作副本", FormatBytes(usage.WorkingCopyBytes), "当前格式恢复工作文件"));
            PhysicalUsage.Add(new("Backup / 内部备份与恢复", FormatBytes(usage.BackupBytes), "包括迁移备份与恢复材料"));
            PhysicalUsage.Add(new("Cache / 缓存", FormatBytes(usage.CacheBytes), "应用缓存"));
            PhysicalUsage.Add(new("Logs / 日志", FormatBytes(usage.LogBytes), "应用业务日志"));
            PhysicalUsage.Add(new("Temp / 临时文件", FormatBytes(usage.TempBytes), "受管理的临时文件"));
            PhysicalUsage.Add(new("其他数据与运行开销", FormatBytes(usage.OtherBytes), "未分类数据也计入总占用"));
            var payloadUnavailable = usage.Diagnostics.Any(code => code.StartsWith("DatabaseUsage:", StringComparison.Ordinal));
            void Payload(string name, long bytes) => DatabasePayloads.Add(new(name,
                payloadUnavailable ? "未能读取" : FormatBytes(bytes), "逻辑占用，包含于 Database，不另加到总计"));
            Payload("Snapshot / 快照", usage.SnapshotBytes);
            Payload("Comparison / 历史比对", usage.ComparisonBytes);
            Payload("Format Restore / 格式恢复记录", usage.RestoreBytes);
            if (confirmedSource is not null && confirmedSource != usage.Root) ClearConfirmation();
            return usage;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            CurrentPath = provider!.CurrentDataRoot; TotalSize = "无法统计"; SampledAt = "";
            PhysicalUsage.Clear(); DatabasePayloads.Clear();
            UsageMessage = "无法读取当前数据占用，请检查数据位置并重试。诊断：" + error.GetType().Name;
            ClearConfirmation();
            return null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ValidateTargetAsync()
    {
        if (validator is null || usageService is null) return;
        ClearConfirmation(); Message = ""; IsBusy = true;
        var selected = TargetPath;
        try
        {
            var usage = await ReadUsageAsync(CancellationToken.None);
            if (usage is not { IsComplete: true }) { Message = "完整统计后才能验证迁移空间，请先刷新并解决统计问题。"; return; }
            var result = await Task.Run(() => validator.Validate(new(selected, usage.Root.Path, usage.TotalBytes, usage.DatabaseBytes)));
            if (selected != TargetPath) { Message = "目标位置已改变，请重新验证。"; return; }
            if (!result.IsValid) { Message = Explain(result.Code); return; }
            TargetPath = result.NormalizedPath!;
            confirmedSource = usage.Root; confirmedTarget = result.NormalizedPath;
            ConfirmationSource = usage.Root.Path; ConfirmationTarget = confirmedTarget!;
            SpaceMessage = $"预计至少需要 {FormatBytes(result.RequiredBytes)}；目标磁盘可用 {FormatBytes(result.AvailableBytes)}。";
            ShowConfirmation = true;
        }
        catch (Exception error) { Message = Explain(error.GetType().Name); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm), IncludeCancelCommand = true)]
    private async Task MigrateAsync(CancellationToken token)
    {
        if (migration is null || confirmedSource is null || confirmedTarget is null) return;
        var source = confirmedSource; var target = confirmedTarget;
        if (provider!.Descriptor != source || TargetPath != target)
        { ClearConfirmation(); Message = "数据位置或目标已改变，请重新验证后确认。"; return; }
        IsBusy = true; IsMigrating = true; Message = "请等待操作完成。提交前可取消，正在提交时将完成安全切换。";
        ShowConfirmation = false;
        try
        {
            // The foundation owns the maintenance barrier, journal, validation and durable commit.
            var result = await Task.Run(() => migration.MigrateAsync(target, token), CancellationToken.None);
            RecoveryRequired = result.Status == StorageMigrationStatus.NeedsReview;
            CurrentPath = provider.CurrentDataRoot;
            Message = result.Status switch
            {
                StorageMigrationStatus.Completed => $"数据位置已更改，无需重启。旧数据已保留在：{result.SourceRoot}。" +
                    (result.Code == "CommittedReceiptNeedsRecovery" ? "切换已提交，操作记录将在下次启动核验。" : ""),
                StorageMigrationStatus.Cancelled => $"已取消，当前数据位置未更改。旧数据保留在：{result.SourceRoot}。目标可能保留本次未完成的副本。",
                StorageMigrationStatus.NeedsReview => "无法确认切换状态，数据写入已暂停。请正常关闭后重启以核验恢复；不要移动或删除源目录和目标目录。",
                _ => $"更改失败（{StageName(result.Stage)}）：{Explain(result.Code)} 旧数据保留在：{result.SourceRoot}。请解决原因后选择新的空目录重试。",
            };
            if (!RecoveryRequired) await ReadUsageAsync(CancellationToken.None); // Cancellation cannot undo a committed root.
            else { TotalSize = "需恢复核验"; PhysicalUsage.Clear(); DatabasePayloads.Clear(); }
        }
        catch (Exception error)
        {
            // A service/transport exception does not establish whether durable commit happened.
            RecoveryRequired = true; TotalSize = "需恢复核验";
            PhysicalUsage.Clear(); DatabasePayloads.Clear();
            Message = "无法核实迁移结果，请正常关闭后重启以核验恢复。请保留源与目标数据。诊断：" + error.GetType().Name;
        }
        finally { ClearConfirmation(); IsMigrating = false; IsBusy = false; }
    }

    [RelayCommand] private void DismissConfirmation() => ClearConfirmation();
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task OpenFolderAsync()
    {
        if (provider is null || folders is null) return;
        IsBusy = true;
        try
        {
            await ReadUsageAsync(CancellationToken.None); // Acquire a fresh generation, including another process's committed root.
            folders.Open(provider.CurrentDataRoot);
        }
        catch (Exception error) { Message = "打开数据文件夹失败，请检查当前路径。诊断：" + error.GetType().Name; }
        finally { IsBusy = false; }
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(CultureInfo.CurrentCulture) + " B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.##", CultureInfo.CurrentCulture) + " KiB",
        < 1024L * 1024 * 1024 => (bytes / (1024d * 1024)).ToString("0.##", CultureInfo.CurrentCulture) + " MiB",
        _ => (bytes / (1024d * 1024 * 1024)).ToString("0.##", CultureInfo.CurrentCulture) + " GiB",
    };
    private static string StageName(StorageMigrationStage stage) => stage switch
    {
        StorageMigrationStage.Validating => "验证位置", StorageMigrationStage.Quiescing => "等待正在使用的数据",
        StorageMigrationStage.BackingUp => "备份数据库", StorageMigrationStage.Copying => "复制数据",
        StorageMigrationStage.ValidatingCopy => "校验副本", StorageMigrationStage.Relocating => "更新托管引用",
        StorageMigrationStage.Finalizing => "准备目标目录", StorageMigrationStage.PreparingRuntime => "重新打开数据",
        StorageMigrationStage.Committing => "提交切换", _ => "完成核验",
    };
    private static string Explain(string? code) => code switch
    {
        "AbsolutePathRequired" => "请选择完整的本机绝对目录。",
        "NetworkOrDevicePathUnsupported" or "NetworkDiskUnsupported" => "暂不支持网络共享、映射网络盘或设备路径。",
        "DiskRootUnsupported" => "不能直接使用磁盘根目录，请选择专用数据文件夹。",
        "InstallationOverlap" => "数据目录不能位于安装目录中，也不能包含安装目录。",
        "SourceOverlap" => "新目录不能与当前数据目录相同，或互为父子目录。",
        "TemporaryDirectoryUnsupported" => "不能将数据存放在系统临时目录。",
        "KnownSyncDirectoryUnsupported" => "暂不支持 OneDrive 等已识别同步目录。",
        "TargetNotEmpty" => "目标目录不是空目录，请选择新的空文件夹；已有数据不会被覆盖。",
        "InsufficientSpace" => "目标磁盘空间不足，请释放空间或选择其他固定磁盘。",
        "PermissionDenied" or "UnauthorizedAccessException" => "当前用户无足够的读写或重命名权限，请选择可写目录。",
        "PathAccessOrLinkFailure" => "目录不可访问或包含链接，请选择不含链接的本机固定磁盘目录。",
        "RemovableDiskUnsupported" or "UnknownDiskUnsupported" => "请选择可验证的本机固定磁盘，暂不支持移动盘。",
        "TimeoutException" or "InvalidOperationException" => "数据正在被使用，请等待当前操作完成并关闭其他实例或 Word/WPS 后重试。",
        "IOException" => "读写失败，请检查磁盘空间、目录权限以及文件是否被其他程序占用。",
        "InvalidPath" or "UnsafePathComponent" or "UnsupportedPath" => "目录格式不受支持，请选择普通本机文件夹。",
        _ => "无法完成验证或更改。诊断：" + (code ?? "Unknown"),
    };
}
