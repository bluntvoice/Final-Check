using FinalCheck.App.ViewModels;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Management;

namespace FinalCheck.App.Tests;
public sealed class ProjectComparisonViewModelTests
{
    private sealed class Service(VersionManagementTests.Service versions) : IProjectComparisonService
    {
        public Guid? Baseline { get; private set; }
        public ProjectBaselineType? LastType { get; set; }
        public ProjectComparisonSelection? Selection { get; private set; }
        public Task SetBaselineAsync(Guid projectId, Guid versionId, CancellationToken token = default)
        { var version = versions.Versions.Single(x => x.ContractVersionId == versionId); if (version.ProjectId != projectId || version.Role != ContractVersionRole.Own) throw new ArgumentException("只有本项目我方版本可以设为基准。"); Baseline = versionId; return Task.CompletedTask; }
        public Task<ProjectComparisonChoices> ChoicesAsync(Guid projectId, Guid currentVersionId, CancellationToken token = default) => Task.FromResult(new ProjectComparisonChoices(projectId, currentVersionId,
            [new(ProjectBaselineType.Own, Baseline ?? Guid.NewGuid(), "当前我方", true), new(ProjectBaselineType.Template, Guid.NewGuid(), "当前模板", true)], LastType, "建议当前我方基准，不与上一版对方比较。"));
        public Task<ComparisonWorkflowResult> CompareAsync(ProjectComparisonSelection selection, IProgress<string>? progress = null, CancellationToken token = default) { Selection = selection; LastType = selection.BaselineType; return Task.FromResult(ComparisonExecutionTests.Result()); }
        public Task<IReadOnlyList<ProjectComparisonHistory>> HistoryAsync(Guid projectId, Guid? versionId = null, int offset = 0, int limit = 20, CancellationToken token = default) => Task.FromResult<IReadOnlyList<ProjectComparisonHistory>>([]);
    }
    [Fact] public async Task NewOwnPromptDefaultsNoAndExplicitChoiceIsRequired()
    {
        var versions = new VersionManagementTests.Service(); var service = new Service(versions); var vm = new VersionManagementViewModel(versions, service); var id = Guid.NewGuid(); await vm.OpenProjectAsync(id);
        await vm.AcceptFilesAsync(["own.docx"]); vm.ImportQueue[0].Role = "我方版本"; await vm.ImportCommand.ExecuteAsync(null); Assert.True(vm.BaselinePrompt); Assert.Null(vm.NewOwnBaseline); Assert.Null(service.Baseline);
        await vm.ConfirmNewBaselineCommand.ExecuteAsync(null); Assert.Null(service.Baseline); vm.KeepBaselineCommand.Execute(null); Assert.False(vm.BaselinePrompt);
        await vm.SelectVersionAsync(vm.Versions[0]); await vm.SetBaselineCommand.ExecuteAsync(null); Assert.Equal(versions.Versions[0].ContractVersionId, service.Baseline);
    }
    [Fact] public async Task RememberedTypePreselectsWithoutExecutingAndOwnSuggestionRemainsVisible()
    {
        var versions = new VersionManagementTests.Service(); var id = Guid.NewGuid(); await versions.ImportAsync(id, [new("opponent.docx", ContractVersionRole.Counterparty, 1, "")]);
        var service = new Service(versions) { LastType = ProjectBaselineType.Template }; var vm = new VersionManagementViewModel(versions, service); await vm.OpenProjectAsync(id); await vm.SelectVersionAsync(vm.Versions[0]);
        Assert.Equal(ProjectBaselineType.Template, vm.SelectedBaseline!.Type); Assert.Contains("当前我方", vm.BaselineRecommendation); Assert.Null(service.Selection);
        ComparisonWorkflowResult? displayed = null; vm.Completed += result => { displayed = result; return Task.CompletedTask; };
        await vm.CompareCommand.ExecuteAsync(null); Assert.NotNull(displayed); Assert.Equal(vm.SelectedVersion!.Version.ContractVersionId, service.Selection!.CurrentVersionId); Assert.Equal(ProjectBaselineType.Template, service.Selection.BaselineType);
    }
    [Fact] public async Task CounterpartyBaselineIsRejectedWithoutChangingExistingBaseline()
    {
        var versions = new VersionManagementTests.Service(); var id = Guid.NewGuid(); await versions.ImportAsync(id, [new("own.docx", ContractVersionRole.Own, 1, ""), new("other.docx", ContractVersionRole.Counterparty, 1, "")]);
        var service = new Service(versions); await service.SetBaselineAsync(id, versions.Versions[0].ContractVersionId); var vm = new VersionManagementViewModel(versions, service); await vm.OpenProjectAsync(id); await vm.SelectVersionAsync(vm.Versions[1]); await vm.SetBaselineCommand.ExecuteAsync(null);
        Assert.Equal(versions.Versions[0].ContractVersionId, service.Baseline); Assert.Contains("只有本项目我方", vm.Message);
    }
}
