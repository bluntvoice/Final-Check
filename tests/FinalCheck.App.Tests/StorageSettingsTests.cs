using FinalCheck.App.ViewModels;
using FinalCheck.Core.Storage;

namespace FinalCheck.App.Tests;

public sealed class StorageSettingsTests
{
    [Fact]
    public async Task ValidationNeedsExplicitConfirmationAndEditingTargetInvalidatesIt()
    {
        var services = new FakeStorage(); var vm = services.Model();
        vm.TargetPath = services.Target;
        await vm.ValidateTargetCommand.ExecuteAsync(null);
        Assert.True(vm.ShowConfirmation); Assert.Equal(0, services.MigrationCalls);
        Assert.Equal(services.Root.Path, vm.ConfirmationSource);
        Assert.Equal(services.Target, vm.ConfirmationTarget);
        vm.TargetPath += "-edited";
        Assert.False(vm.ShowConfirmation); Assert.False(vm.MigrateCommand.CanExecute(null));
        await vm.MigrateCommand.ExecuteAsync(null);
        Assert.Equal(0, services.MigrationCalls);
    }

    [Fact]
    public async Task PartialDatabaseUsageCannotAuthorizeMigrationAndDoesNotPretendPayloadIsZero()
    {
        var services = new FakeStorage { Partial = true }; var vm = services.Model(); vm.TargetPath = services.Target;
        await vm.ValidateTargetCommand.ExecuteAsync(null);
        Assert.False(vm.ShowConfirmation); Assert.Equal(0, services.ValidationCalls);
        Assert.StartsWith("已统计", vm.TotalSize);
        Assert.All(vm.DatabasePayloads, row => Assert.Equal("未能读取", row.Size));
        Assert.Contains("完整统计", vm.Message);
    }

    [Fact]
    public async Task CompletedCommitRefreshesActiveRootEvenWhenCancellationArrivesAtCommit()
    {
        var services = new FakeStorage(); var vm = services.Model(); vm.TargetPath = services.Target;
        await vm.ValidateTargetCommand.ExecuteAsync(null);
        services.DuringMigration = () => vm.MigrateCommand.Cancel();
        await vm.MigrateCommand.ExecuteAsync(null);
        Assert.Equal(services.Target, vm.CurrentPath);
        Assert.Contains("旧数据已保留", vm.Message);
        Assert.Contains(services.Source, vm.Message);
        Assert.False(vm.IsBusy); Assert.False(vm.ShowConfirmation);
        Assert.True(services.UsageCalls >= 2);
        Assert.Equal(7, vm.PhysicalUsage.Count); Assert.Equal(3, vm.DatabasePayloads.Count);
        Assert.All(vm.DatabasePayloads, row => Assert.Contains("不另加到总计", row.Description));
        await vm.OpenFolderCommand.ExecuteAsync(null);
        Assert.Equal(services.Target, services.OpenedPath);
    }

    [Theory]
    [InlineData(StorageMigrationStatus.Failed, false)]
    [InlineData(StorageMigrationStatus.Cancelled, false)]
    [InlineData(StorageMigrationStatus.NeedsReview, true)]
    public async Task FailureOrCancellationPreservesSourceAndAmbiguousCommitRequiresRecovery(StorageMigrationStatus status, bool recovery)
    {
        var services = new FakeStorage { Outcome = status }; var vm = services.Model(); vm.TargetPath = services.Target;
        await vm.ValidateTargetCommand.ExecuteAsync(null);
        await vm.MigrateCommand.ExecuteAsync(null);
        Assert.Equal(services.Source, vm.CurrentPath);
        Assert.Equal(recovery, vm.RecoveryRequired);
        Assert.Equal(!recovery, vm.CanEdit);
        Assert.False(vm.ShowConfirmation); Assert.False(vm.IsMigrating);
        if (status == StorageMigrationStatus.Failed) Assert.Contains("复制数据", vm.Message);
    }

    [Fact]
    public async Task ServiceExceptionDoesNotClaimTheOldRootIsStillAuthoritative()
    {
        var services = new FakeStorage { ThrowMigration = true }; var vm = services.Model(); vm.TargetPath = services.Target;
        await vm.ValidateTargetCommand.ExecuteAsync(null);
        await vm.MigrateCommand.ExecuteAsync(null);
        Assert.True(vm.RecoveryRequired); Assert.Contains("核验恢复", vm.Message);
        Assert.DoesNotContain("未更改", vm.Message);
    }

    [Fact]
    public async Task MigrationSuccessAndStatisticsFailureRemainDifferentResults()
    {
        var services = new FakeStorage(); var vm = services.Model(); vm.TargetPath = services.Target;
        await vm.ValidateTargetCommand.ExecuteAsync(null);
        services.DuringMigration = () => services.ThrowUsage = true;
        await vm.MigrateCommand.ExecuteAsync(null);
        Assert.Equal(services.Target, vm.CurrentPath);
        Assert.Contains("数据位置已更改", vm.Message);
        Assert.Equal("无法统计", vm.TotalSize); Assert.Empty(vm.PhysicalUsage);
        Assert.False(vm.RecoveryRequired);
    }

    [Fact]
    public async Task RootGenerationChangeInvalidatesPreviouslyConfirmedMigration()
    {
        var services = new FakeStorage(); var vm = services.Model(); vm.TargetPath = services.Target;
        await vm.ValidateTargetCommand.ExecuteAsync(null);
        services.Root = services.Root with { Generation = 2 };
        await vm.MigrateCommand.ExecuteAsync(null);
        Assert.Equal(0, services.MigrationCalls); Assert.False(vm.ShowConfirmation);
        Assert.Contains("重新验证", vm.Message);
    }

    [Fact]
    public async Task NavigationStaysOnStorageUntilMigrationCompletes()
    {
        var services = new FakeStorage(); var vm = services.Model(); vm.TargetPath = services.Target;
        var main = new MainViewModel(new(), storage: vm);
        main.NavigateCommand.Execute("storage");
        await vm.RefreshCommand.ExecutionTask!;
        await vm.ValidateTargetCommand.ExecuteAsync(null);
        services.DuringMigration = () =>
        {
            main.NavigateCommand.Execute("home");
            Assert.True(main.IsStorage); Assert.False(main.IsOther);
        };
        await vm.MigrateCommand.ExecuteAsync(null);
        main.NavigateCommand.Execute("home"); Assert.True(main.IsHome);
    }

    internal sealed class FakeStorage : IDataRootProvider, IStorageUsageService, IDataRootValidator, IDataRootMigrationService, IStorageFolderOpener
    {
        public string Source { get; } = Path.Combine(Path.GetTempPath(), "storage-source");
        public string Target { get; } = Path.Combine(Path.GetTempPath(), "storage-target");
        public DataRootDescriptor Root { get; set; } = new(Path.Combine(Path.GetTempPath(), "storage-source"), Guid.NewGuid(), 1, 1);
        public bool Partial { get; set; }
        public bool ThrowUsage { get; set; }
        public bool ThrowMigration { get; set; }
        public int UsageCalls { get; private set; }
        public int ValidationCalls { get; private set; }
        public int MigrationCalls { get; private set; }
        public string? OpenedPath { get; private set; }
        public Action? DuringMigration { get; set; }
        public TaskCompletionSource? PauseMigration { get; set; }
        public StorageMigrationStatus Outcome { get; set; } = StorageMigrationStatus.Completed;
        public StorageSettingsViewModel Model() => new(this, this, this, this, this);
        public DataRootDescriptor Descriptor => Root;
        public string CurrentDataRoot => Root.Path;
        public string DatabasePath => Path.Combine(Root.Path, "finalcheck.db");
        public string SnapshotPath => DatabasePath;
        public string WorkingCopyPath => Root.Path;
        public string BackupPath => Root.Path;
        public string CachePath => Root.Path;
        public string LogPath => Root.Path;
        public string TempPath => Root.Path;
        public Task<StorageUsage> CalculateAsync(CancellationToken cancellationToken = default)
        {
            UsageCalls++;
            if (ThrowUsage) throw new IOException();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new StorageUsage(Root, DateTimeOffset.UtcNow, 4096, 1024, 512, 256, 10, 20, 30, 40, 50, 60,
                4306, !Partial, Partial ? ["DatabaseUsage:IOException"] : []));
        }
        public DataRootValidationResult Validate(DataRootValidationRequest request)
        { ValidationCalls++; return new(true, request.Path, "Validated", 64 * 1024 * 1024, 1024L * 1024 * 1024); }
        public async Task<StorageMigrationResult> MigrateAsync(string target, CancellationToken cancellationToken = default)
        {
            MigrationCalls++; DuringMigration?.Invoke();
            if (PauseMigration is not null) await PauseMigration.Task;
            if (ThrowMigration) throw new IOException();
            if (Outcome == StorageMigrationStatus.Completed) Root = new(target, Guid.NewGuid(), 1, Root.Generation + 1);
            return new StorageMigrationResult(Outcome, StorageMigrationStage.Copying, Guid.NewGuid(), "IOException", Source, target);
        }
        public void Open(string absoluteDirectory) => OpenedPath = absoluteDirectory;
    }
}
