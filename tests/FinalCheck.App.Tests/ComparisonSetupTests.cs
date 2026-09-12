using FinalCheck.App.ViewModels;
using FinalCheck.Core.Comparisons;

namespace FinalCheck.App.Tests;

public sealed class ComparisonSetupTests
{
    private sealed class Inspector : IComparisonFileInspector
    {
        public Task<ComparisonFile> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            Path.GetExtension(path) == ".docx" ? Task.FromResult(new ComparisonFile(path, Path.GetFileName(path), 123, DateTimeOffset.UnixEpoch, "abc")) :
            throw new ArgumentException("DOCX only");
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
