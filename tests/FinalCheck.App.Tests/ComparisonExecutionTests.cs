using FinalCheck.App.ViewModels;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.App.Tests;

public sealed class ComparisonExecutionTests
{
    private static ComparisonFile File(string name) => new("/" + name + ".docx", name + ".docx", 10, DateTimeOffset.UnixEpoch, new string('a', 64));
    internal static ComparisonWorkflowResult Result(bool partial = false) => new(
        new ComparisonRecord(1, Guid.NewGuid(), File("baseline"), File("current"), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, new Dictionary<string, ComparisonReviewState>()),
        DocumentSnapshot.Empty, DocumentSnapshot.Empty with { ParseStatus = partial ? DocumentParseStatus.Partial : DocumentParseStatus.Complete },
        new ComparisonResult(1, new("b", "c", 2, 2, "test"), [], [], [], [], ComparisonStatistics.Empty));
    private sealed class Workflow(bool same = false, bool partial = false, Exception? error = null, bool wait = false) : IComparisonWorkflowService
    {
        public int Executions { get; private set; }
        public Task<ComparisonInputValidation> ValidateAsync(ComparisonFile baseline, ComparisonFile current, CancellationToken cancellationToken = default) => Task.FromResult(new ComparisonInputValidation(baseline, current, false, same, false));
        public async Task<ComparisonWorkflowResult> ExecuteAsync(ComparisonInputValidation input, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            Executions++; if (wait) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (error is not null) throw error; return Result(partial);
        }
        public Task<IReadOnlyList<ComparisonRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ComparisonRecord>>([]);
        public Task<ComparisonWorkflowResult?> LoadAsync(Guid recordId, CancellationToken cancellationToken = default) => Task.FromResult<ComparisonWorkflowResult?>(null);
    }
    private static ComparisonSetupViewModel VM(Workflow service) => new(null, service) { BaselineFile = File("baseline"), CurrentFile = File("current") };
    [Fact] public async Task SuccessfulExecutionCompletesSessionAndReleasesBusyState()
    {
        using var vm = VM(new()); var completed = false; vm.Completed += _ => completed = true;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(completed); Assert.False(vm.IsBusy); Assert.NotNull(vm.Session.ResultId); Assert.Equal(ComparisonSessionStatus.Completed, vm.Session.Status);
    }
    [Fact] public async Task IdenticalNeedsExplicitContinueOrCancelBeforeEngineRuns()
    {
        var service = new Workflow(true); using var vm = VM(service);
        await vm.StartCommand.ExecuteAsync(null); Assert.True(vm.IdenticalPrompt); Assert.False(vm.CanStart); Assert.Equal(0, service.Executions);
        vm.CancelCommand.Execute(null); Assert.False(vm.IdenticalPrompt); Assert.Equal(ComparisonSessionStatus.Cancelled, vm.Session.Status);
        await vm.StartCommand.ExecuteAsync(null); await vm.ContinueIdenticalCommand.ExecuteAsync(null);
        Assert.Equal(1, service.Executions); Assert.Equal(ComparisonSessionStatus.Completed, vm.Session.Status);
    }
    [Fact] public async Task PartialWaitsForContinueAndIsNotSilentlyHidden()
    {
        using var vm = VM(new(partial: true)); var completed = false; vm.Completed += _ => completed = true;
        await vm.StartCommand.ExecuteAsync(null); Assert.False(completed); Assert.Contains("可能不完整", vm.Message);
        vm.ViewPartialCommand.Execute(null); Assert.True(completed);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task ErrorsAndEncryptionShowFriendlyMessagesWithoutStacks(bool encrypted)
    {
        using var vm = VM(new(error: encrypted ? new DocumentParseException(DocumentParseErrorKind.Encrypted, "secret-stack") : new InvalidOperationException("secret-stack")));
        await vm.StartCommand.ExecuteAsync(null); Assert.Equal(ComparisonSessionStatus.Failed, vm.Session.Status);
        Assert.DoesNotContain("secret-stack", vm.Message); Assert.DoesNotContain("secret-stack", vm.TechnicalDetails);
        if (encrypted) Assert.Contains("加密", vm.Message);
    }
    [Fact] public async Task CancellationBlocksDuplicatesAndNavigationRequiresDecision()
    {
        var service = new Workflow(wait: true); using var vm = VM(service); var main = new MainViewModel(vm);
        main.NavigateCommand.Execute("compare"); var execution = vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsExecuting); Assert.False(vm.CanStart); Assert.False(vm.StartCommand.CanExecute(null));
        main.NavigateCommand.Execute("about"); Assert.True(main.LeavePrompt); Assert.True(main.IsSetup);
        main.StayCommand.Execute(null); Assert.True(vm.IsExecuting);
        main.NavigateCommand.Execute("about"); main.ConfirmLeaveCommand.Execute(null); await execution;
        Assert.Equal(ComparisonSessionStatus.Cancelled, vm.Session.Status); Assert.False(vm.IsBusy); Assert.True(main.IsOther); Assert.Equal(1, service.Executions);
    }
}
