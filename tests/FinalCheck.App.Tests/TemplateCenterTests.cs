using FinalCheck.App.ViewModels;
using FinalCheck.Core.Management;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.App.Tests;

public sealed class TemplateCenterTests
{
    internal sealed class Service : ITemplateService
    {
        private readonly Template template = new(Guid.NewGuid(), "代理协议", "代理", true, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "");
        public TemplateDetails Detail { get; }
        public bool DeleteConfirmed { get; private set; }
        public Service()
        {
            Detail = new(template, [new(Guid.NewGuid(), template.TemplateId, "1.0.0", new(Path.GetFullPath("source.docx"), "source.docx", 1, DateTimeOffset.UtcNow, new('A', 64)), Guid.NewGuid(), true, DateTimeOffset.UtcNow, DocumentParseStatus.Complete)]);
        }
        public Task<IReadOnlyList<Template>> ListAsync(int offset = 0, int limit = 100, CancellationToken token = default) => Task.FromResult<IReadOnlyList<Template>>([template]);
        public Task<TemplateDetails> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult(Detail);
        public Task<DocumentSnapshot> LoadSnapshotAsync(Guid versionId, CancellationToken token = default) => Task.FromResult(DocumentSnapshot.Empty);
        public Task<TemplateDetails> ImportAsync(Guid? templateId, string name, string contractType, string version, string path, CancellationToken token = default) => Task.FromResult(Detail);
        public Task UpdateAsync(Guid id, string name, string contractType, string notes, bool enabled, CancellationToken token = default) => Task.CompletedTask;
        public Task SetCurrentAsync(Guid id, Guid versionId, CancellationToken token = default) => Task.CompletedTask;
        public Task RelinkAsync(Guid versionId, string path, CancellationToken token = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ComparisonFile>> FindRelinkCandidatesAsync(TemplateVersion version, CancellationToken token = default) => Task.FromResult<IReadOnlyList<ComparisonFile>>([]);
        public Task<TemplateReferences> ReferencesAsync(Guid id, CancellationToken token = default) => Task.FromResult(new TemplateReferences(["既有项目"], 2));
        public Task DeleteAsync(Guid id, bool confirmed, CancellationToken token = default) { DeleteConfirmed = confirmed; return Task.CompletedTask; }
    }
    [Fact] public async Task PickerAndDropAcceptExactlyOneDocxAndSuggestName()
    {
        var vm = new TemplateCenterViewModel(); await vm.AcceptFilesAsync(["合同模板.docx"]); Assert.Equal("合同模板", vm.Name);
        await vm.AcceptFilesAsync(["one.docx", "two.docx"]); Assert.Equal("合同模板.docx", vm.SourcePath); Assert.Contains("一个 DOCX", vm.Message);
        await vm.AcceptFilesAsync(["other.pdf"]); Assert.Equal("合同模板.docx", vm.SourcePath);
    }
    [Fact] public async Task SelectionLoadsOnlyRequestedVersionsAndSuggestsNextVersion()
    {
        var service = new Service(); var vm = new TemplateCenterViewModel(service); await vm.RefreshCommand.ExecuteAsync(null);
        await vm.SelectTemplateAsync(vm.Templates[0]); Assert.Single(vm.Versions); Assert.Equal("代理", vm.ContractType); Assert.Equal("1.0.1", vm.Version);
        await vm.FindSourceCommand.ExecuteAsync(null); Assert.Contains("手动选择", vm.Message);
        await vm.PreviewCommand.ExecuteAsync(null); Assert.Contains("冻结历史", vm.Message);
    }
    [Fact] public async Task DeleteShowsReferencesAndNeedsSeparateConfirmation()
    {
        var service = new Service(); var vm = new TemplateCenterViewModel(service); await vm.SelectTemplateAsync(service.Detail.Template);
        await vm.ConfirmDeleteCommand.ExecuteAsync(null); Assert.False(service.DeleteConfirmed);
        await vm.RequestDeleteCommand.ExecuteAsync(null); Assert.True(vm.DeletePrompt); Assert.Contains("既有项目", vm.Message); Assert.Contains("历史比对：2", vm.Message);
        vm.CancelDeleteCommand.Execute(null); Assert.False(vm.DeletePrompt); Assert.False(service.DeleteConfirmed);
        await vm.RequestDeleteCommand.ExecuteAsync(null); await vm.ConfirmDeleteCommand.ExecuteAsync(null); Assert.True(service.DeleteConfirmed);
    }
    [Fact] public async Task PersistedConfirmedDisplaysReviewedWithoutAcceptingContractChange()
    {
        var result = ComparisonResultsTests.Result(ComparisonResultsTests.Change());
        var stored = result.Record with { ReviewStates = new Dictionary<string, ComparisonReviewState> { ["one"] = ComparisonReviewState.Confirmed } };
        var vm = new ComparisonResultsViewModel(result with { Record = stored }); Assert.Equal("已审阅", vm.Changes[0].ReviewLabel);
        vm.StatusFilter = "已审阅"; Assert.Single(vm.Entries); Assert.DoesNotContain("已确认", vm.StatusOptions);
        Assert.Equal(ComparisonReviewState.Confirmed, vm.Changes[0].ReviewState);
        await Task.CompletedTask;
    }
}
