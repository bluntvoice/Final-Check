using FinalCheck.App.ViewModels;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Management;

namespace FinalCheck.App.Tests;
public sealed class VersionManagementTests
{
    internal sealed class Service : IContractVersionService
    {
        public List<ContractVersion> Versions { get; } = [];
        public IReadOnlyList<VersionImport>? Imported { get; private set; }
        public bool PreviewLoaded { get; private set; }
        public Task<IReadOnlyList<ContractVersion>> ListAsync(Guid projectId, bool chronological = false, int offset = 0, int limit = 20, CancellationToken token = default) => Task.FromResult<IReadOnlyList<ContractVersion>>(Versions.Skip(offset).Take(limit).ToArray());
        public Task<IReadOnlyList<NegotiationRound>> RoundsAsync(Guid projectId, CancellationToken token = default) => Task.FromResult<IReadOnlyList<NegotiationRound>>(Versions.Select(v => v.RoundNumber).Distinct().Select(n => new NegotiationRound(Guid.NewGuid(), projectId, n)).ToArray());
        public Task<IReadOnlyList<ContractVersion>> SameContentAsync(Guid projectId, string sha256, CancellationToken token = default) => Task.FromResult<IReadOnlyList<ContractVersion>>(Versions.Where(v => v.Source.Sha256 == sha256).ToArray());
        public Task<ComparisonFile> InspectAsync(string path, CancellationToken token = default) => Task.FromResult(new ComparisonFile(path, Path.GetFileName(path), 1, DateTimeOffset.UtcNow, path.Contains("duplicate", StringComparison.Ordinal) ? "same" : path));
        public async Task<IReadOnlyList<ContractVersion>> ImportAsync(Guid projectId, IReadOnlyList<VersionImport> versions, CancellationToken token = default)
        {
            Imported = versions; var result = new List<ContractVersion>(); foreach (var input in versions)
            { var file = await InspectAsync(input.Path, token); var row = new ContractVersion(Guid.NewGuid(), projectId, file, Guid.NewGuid(), input.Role, input.RoundNumber, false, DateTimeOffset.UtcNow, input.Notes, file, null, DocumentParseStatus.Complete, Versions.Count + 1); result.Add(row); Versions.Add(row); }
            return result;
        }
        public Task<ContractVersion> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult(Versions.Single(v => v.ContractVersionId == id));
        public Task<DocumentSnapshot> LoadSnapshotAsync(Guid id, CancellationToken token = default) { PreviewLoaded = true; return Task.FromResult(DocumentSnapshot.Empty); }
        public Task RelinkAsync(Guid id, string path, CancellationToken token = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ComparisonFile>> FindRelinkCandidatesAsync(ContractVersion version, CancellationToken token = default) => Task.FromResult<IReadOnlyList<ComparisonFile>>([version.Source]);
    }
    private sealed class Comparisons(Service versions) : IProjectComparisonService
    {
        public Task SetBaselineAsync(Guid projectId, Guid versionId, CancellationToken token = default) => throw new NotSupportedException();
        public Task<ProjectComparisonChoices> ChoicesAsync(Guid projectId, Guid currentVersionId, CancellationToken token = default) =>
            Task.FromResult(new ProjectComparisonChoices(projectId, currentVersionId, versions.Versions.Where(x => x.ContractVersionId != currentVersionId)
                .Select(x => new ProjectBaselineOption(ProjectBaselineType.Version, x.ContractVersionId, x.VersionLabel, false)).ToArray(), null, "可选择已有版本作为基准。"));
        public Task<ComparisonWorkflowResult> CompareAsync(ProjectComparisonSelection selection, IProgress<string>? progress = null, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectComparisonHistory>> HistoryAsync(Guid projectId, Guid? versionId = null, int offset = 0, int limit = 20, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<ProjectComparisonHistory>>([]);
    }
    [Fact] public async Task MultiFileDropQueueDefaultsToUnspecifiedAndAllowsExplicitRoles()
    {
        var service = new Service(); var vm = new VersionManagementViewModel(service); await vm.OpenProjectAsync(Guid.NewGuid());
        await vm.AcceptFilesAsync(["未指定1.docx", "未指定2.docx"]); Assert.All(vm.ImportQueue, x => Assert.Equal("暂未指定", x.Role));
        await vm.ImportCommand.ExecuteAsync(null); Assert.All(service.Imported!, x => Assert.Equal(ContractVersionRole.Unspecified, x.Role));
        await vm.AcceptFilesAsync(["对方文件.docx", "我方文件.docx"]);
        vm.ImportQueue[0].Role = "我方版本"; vm.ImportQueue[1].Role = "对方版本"; await vm.ImportCommand.ExecuteAsync(null);
        Assert.Equal(ContractVersionRole.Own, service.Imported![0].Role); Assert.Equal(ContractVersionRole.Counterparty, service.Imported[1].Role); Assert.All(service.Versions, x => Assert.False(x.IsCurrentBaseline)); Assert.Empty(vm.ImportQueue);
    }
    [Fact] public async Task DuplicateDefaultSkipExplicitImportAndQueueOrderingAreStable()
    {
        var service = new Service(); var vm = new VersionManagementViewModel(service); await vm.OpenProjectAsync(Guid.NewGuid());
        await vm.AcceptFilesAsync(["duplicate-a.docx", "duplicate-b.docx"]); Assert.False(vm.ImportQueue[0].Duplicate); Assert.True(vm.ImportQueue[1].Duplicate);
        vm.MoveUpCommand.Execute(vm.ImportQueue[1]); Assert.False(vm.ImportQueue[0].Duplicate); Assert.True(vm.ImportQueue[1].Duplicate);
        vm.RemoveCommand.Execute(vm.ImportQueue[0]); Assert.False(vm.ImportQueue[0].Duplicate);
        await vm.AcceptFilesAsync(["duplicate-c.docx"]); foreach (var row in vm.ImportQueue) row.Role = "我方版本";
        await vm.ImportCommand.ExecuteAsync(null); Assert.Single(service.Imported!);
        await vm.AcceptFilesAsync(["duplicate-d.docx"]); vm.ImportQueue[0].Role = "对方版本"; vm.ImportQueue[0].AllowDuplicate = true;
        await vm.ImportCommand.ExecuteAsync(null); Assert.True(service.Imported![0].AllowDuplicate);
    }
    [Fact] public async Task NewVersionIsSelectedWithComparisonSuggestionWithoutAutoRunningComparison()
    {
        var service = new Service(); var project = Guid.NewGuid();
        var first = Assert.Single(await service.ImportAsync(project, [new("first.docx", ContractVersionRole.Unspecified, 1, "")]));
        var vm = new VersionManagementViewModel(service, new Comparisons(service)); await vm.OpenProjectAsync(project);
        await vm.AcceptFilesAsync(["second.docx"]); await vm.ImportCommand.ExecuteAsync(null);
        Assert.Equal("V2", vm.SelectedVersion?.Version.VersionLabel);
        Assert.Equal(first.ContractVersionId, vm.SelectedBaseline?.VersionId);
        Assert.Contains("仍需手动点击开始比对", vm.Message);
        Assert.Empty(vm.History);
    }
    [Fact] public async Task CurrentNextExistingRoundsAndLazyPreviewKeepProjectStateSeparated()
    {
        var service = new Service(); var id = Guid.NewGuid(); await service.ImportAsync(id, [new("a.docx", ContractVersionRole.Own, 1, ""), new("b.docx", ContractVersionRole.Counterparty, 2, "")]);
        var vm = new VersionManagementViewModel(service); await vm.OpenProjectAsync(id); Assert.False(service.PreviewLoaded); vm.CurrentRoundCommand.Execute(null); Assert.Equal(2, vm.ImportRound);
        vm.NextRoundCommand.Execute(null); Assert.Equal(3, vm.ImportRound); await vm.AcceptFilesAsync(["c.docx"]); Assert.Equal(3, vm.ImportQueue[0].RoundNumber);
        vm.ImportRound = 1; vm.ApplyRoundCommand.Execute(null); Assert.Equal(1, vm.ImportQueue[0].RoundNumber);
        await vm.SelectVersionAsync(vm.Versions[0]); Assert.False(service.PreviewLoaded); await vm.PreviewCommand.ExecuteAsync(null); Assert.True(service.PreviewLoaded);
        await vm.OpenProjectAsync(null); Assert.Empty(vm.ImportQueue); Assert.Empty(vm.Versions); Assert.False(vm.CanEdit);
    }
    [Fact] public async Task RoundListDeselectAndRefreshDoNotClearNumericEditor()
    {
        var service = new Service(); var id = Guid.NewGuid();
        await service.ImportAsync(id, [new("existing.docx", ContractVersionRole.Own, 2, "")]);
        var vm = new VersionManagementViewModel(service); await vm.OpenProjectAsync(id);
        vm.RoundNumbers.CollectionChanged += (_, _) => vm.ExistingRoundSelection = null;
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.ImportRound); Assert.Equal(2m, vm.ImportRoundInput); Assert.Equal(2, vm.ExistingRoundSelection);
        vm.ExistingRoundSelection = null; Assert.Equal(2m, vm.ImportRoundInput);
        vm.NextRoundCommand.Execute(null);
        Assert.Equal(3, vm.ImportRound); Assert.Equal(3m, vm.ImportRoundInput); Assert.Null(vm.ExistingRoundSelection);
        vm.ExistingRoundSelection = 2; Assert.Equal(2m, vm.ImportRoundInput);
    }
    [Fact] public async Task EmptyFractionalAndOutOfRangeRoundInputsNeverImportWithLastValidRound()
    {
        var service = new Service(); var vm = new VersionManagementViewModel(service); await vm.OpenProjectAsync(Guid.NewGuid());
        vm.ImportRoundInput = null; await vm.AcceptFilesAsync(["new.docx"]);
        Assert.Empty(vm.ImportQueue); Assert.Contains("空白", vm.Message);
        vm.CurrentRoundCommand.Execute(null); Assert.Equal(1m, vm.ImportRoundInput);
        await vm.AcceptFilesAsync(["new.docx"]); var row = vm.ImportQueue[0]; row.Role = "我方版本";
        foreach (var input in new decimal?[] { null, 1.5m, 0m, 10001m })
        {
            row.RoundInput = input; Assert.False(row.HasValidRound);
            await vm.ImportCommand.ExecuteAsync(null); Assert.Null(service.Imported); Assert.Single(vm.ImportQueue);
        }
        vm.ImportRoundInput = 1.5m; vm.ApplyRoundCommand.Execute(null); Assert.Equal(10001m, row.RoundInput);
        vm.ImportRoundInput = 1m; vm.ApplyRoundCommand.Execute(null); Assert.Equal(1m, row.RoundInput);
        Assert.True(row.HasValidRound); await vm.ImportCommand.ExecuteAsync(null); Assert.Equal(1, Assert.Single(service.Imported!).RoundNumber);
    }
}
