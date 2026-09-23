using FinalCheck.App.ViewModels;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Management;

namespace FinalCheck.App.Tests;

public sealed class ComparisonSetupTests
{
    private sealed class Inspector : IComparisonFileInspector
    {
        public Task<ComparisonFile> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            Path.GetExtension(path) == ".docx" ? Task.FromResult(new ComparisonFile(path, Path.GetFileName(path), 123, DateTimeOffset.UnixEpoch, "abc")) :
            throw new ArgumentException("DOCX only");
    }
    private sealed class Recommendations(TemplateRecommendationKind kind) : ITemplateRecommendationService
    {
        public Task<TemplateRecommendation> RecommendAsync(ComparisonFile current, CancellationToken token = default)
        {
            var candidate = new TemplateRecommendationCandidate(Guid.NewGuid(), Guid.NewGuid(), "运输协议", "1.2", true,
                0.93, new("/template.docx", "template.docx", 123, DateTimeOffset.UnixEpoch, "other"));
            return Task.FromResult(new TemplateRecommendation(kind, kind == TemplateRecommendationKind.None ? [] : [candidate], "推荐完成；仍需点击开始比对。"));
        }
        public Task<TemplateRecommendation> RecommendSnapshotAsync(DocumentSnapshot snapshot, string fileName,
            CancellationToken token = default) => RecommendAsync(new(fileName, fileName, 0, DateTimeOffset.UnixEpoch, ""), token);
    }
    private sealed class TemplateWorkflow : IComparisonWorkflowService
    {
        public int Executions { get; private set; }
        public ComparisonIgnoreRules? SavedRules { get; private set; }
        public Task<ComparisonWorkflowResult> ExecuteTemplateAsync(ComparisonFile current, Guid templateVersionId,
            IProgress<string>? progress = null, ComparisonIgnoreRules? ignoreRules = null, CancellationToken cancellationToken = default)
        { Executions++; SavedRules = ignoreRules; return Task.FromResult(ComparisonExecutionTests.Result()); }
        public Task<ComparisonInputValidation> ValidateAsync(ComparisonFile baseline, ComparisonFile current, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ComparisonInputValidation(baseline, current, false, false, false));
        public Task<ComparisonWorkflowResult> ExecuteAsync(ComparisonInputValidation input, IProgress<string>? progress = null,
            ComparisonIgnoreRules? ignoreRules = null, CancellationToken cancellationToken = default) { Executions++; SavedRules = ignoreRules; return Task.FromResult(ComparisonExecutionTests.Result()); }
        public Task<IReadOnlyList<ComparisonRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ComparisonRecord>>([]);
        public Task<ComparisonWorkflowResult?> LoadAsync(Guid recordId, CancellationToken cancellationToken = default) => Task.FromResult<ComparisonWorkflowResult?>(null);
        public Task<ComparisonRecord> UpdateReviewAsync(Guid recordId, IReadOnlyList<string> changeIds, ComparisonReviewState state,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ComparisonRecord> EditReviewAsync(Guid recordId, ComparisonReviewEdit edit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    [Fact] public async Task RecommendedTemplatePreselectsButOnlyExplicitStartRunsComparisonAndManualFileOverrides()
    {
        var workflow = new TemplateWorkflow(); using var vm = new ComparisonSetupViewModel(new Inspector(), workflow,
            new Recommendations(TemplateRecommendationKind.Unique));
        await vm.SelectFilesAsync(false, ["/current.docx"]);
        Assert.NotNull(vm.SelectedTemplate); Assert.True(vm.CanStart); Assert.Equal(0, workflow.Executions);
        Assert.Contains("运输协议", vm.BaselineInfo); Assert.False(vm.ShowTemplateChoices);
        vm.ChangeBaselineCommand.Execute(null); Assert.True(vm.ShowTemplateChoices);
        vm.IgnorePunctuation = true; vm.FormatOptions.Single(x => x.Label == "字号").IsSelected = true;
        await vm.StartCommand.ExecuteAsync(null); Assert.Equal(1, workflow.Executions);
        Assert.True(workflow.SavedRules!.Punctuation); Assert.Contains("Character.FontSizeHalfPoints", workflow.SavedRules.HiddenProperties);
        await vm.SelectFilesAsync(true, ["/manual.docx"]);
        Assert.Null(vm.SelectedTemplate); Assert.Equal("manual.docx", vm.BaselineFile?.Name);
    }
    [Fact] public async Task NoReliableTemplateLeavesManualBaselineRequired()
    {
        var workflow = new TemplateWorkflow(); using var vm = new ComparisonSetupViewModel(new Inspector(), workflow,
            new Recommendations(TemplateRecommendationKind.None));
        await vm.SelectFilesAsync(false, ["/unrelated.docx"]);
        Assert.Null(vm.SelectedTemplate); Assert.False(vm.CanStart); Assert.Equal(0, workflow.Executions);
        await vm.SelectFilesAsync(true, ["/manual.docx"]);
        Assert.True(vm.CanStart); Assert.Null(vm.SelectedTemplate);
    }
    [Fact] public void HomeEntryNavigatesToSetupAndPreservesAbout()
    {
        var main = new MainViewModel(); Assert.True(main.IsHome);
        main.NavigateCommand.Execute("compare"); Assert.True(main.IsSetup);
        main.NavigateCommand.Execute("about"); Assert.True(main.IsOther); Assert.Contains(main.AppVersionLabel, main.CurrentPageDescription);
    }
    [Fact] public async Task SelectDropReplaceRemoveAndSessionStayInSync()
    {
        var vm = new ComparisonSetupViewModel(new Inspector());
        Assert.False(vm.CanStart);
        await vm.SelectFilesAsync(true, ["/baseline.docx"]); Assert.False(vm.CanStart);
        await vm.SelectFilesAsync(false, ["/current.docx"]); Assert.True(vm.CanStart);
        Assert.Contains("/baseline.docx", vm.BaselineInfo); Assert.Contains("SHA-256：已校验", vm.CurrentInfo);
        await vm.SelectFilesAsync(true, ["/replacement.docx"]);
        Assert.Equal(vm.BaselineFile, vm.Session.BaselineFile);
        Assert.Equal(vm.CurrentFile, vm.Session.CurrentFile);
        vm.RemoveCurrentCommand.Execute(null); Assert.False(vm.CanStart); Assert.Null(vm.Session.CurrentFile);
        vm.RemoveBaselineCommand.Execute(null); Assert.Null(vm.BaselineFile);
    }
    [Fact] public async Task InvalidOrMultipleFilesDoNotReplaceValidInput()
    {
        var vm = new ComparisonSetupViewModel(new Inspector());
        await vm.SelectFilesAsync(true, ["/baseline.docx"]);
        await vm.SelectFilesAsync(true, ["/bad.pdf"]); Assert.Contains("只支持", vm.Message); Assert.Equal("baseline.docx", vm.BaselineFile!.Name);
        await vm.SelectFilesAsync(false, ["/one.docx", "/two.docx"]); Assert.Null(vm.CurrentFile);
        Assert.Contains("只选择一个", vm.Message);
    }
    [Fact] public void BusyInputDisablesComparisonAndRemoval()
    {
        var vm = new ComparisonSetupViewModel(new Inspector()) { BaselineFile = new("a", "a", 1, DateTimeOffset.UnixEpoch, "a"), CurrentFile = new("b", "b", 1, DateTimeOffset.UnixEpoch, "b"), IsBusy = true };
        Assert.False(vm.CanStart); vm.RemoveBaselineCommand.Execute(null); Assert.NotNull(vm.BaselineFile);
        Assert.NotEqual(Guid.Empty, vm.Session.SessionId);
    }
}
