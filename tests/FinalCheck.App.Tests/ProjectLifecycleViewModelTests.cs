using FinalCheck.App.ViewModels;
using FinalCheck.Core.Management;

namespace FinalCheck.App.Tests;
public sealed class ProjectLifecycleViewModelTests
{
    private sealed class Lifecycle(ProjectsTests.Service projects) : IProjectLifecycleService
    {
        public bool Deleted { get; private set; }
        public Task<ProjectSummary> SummaryAsync(Guid projectId, CancellationToken token = default) => Task.FromResult(new ProjectSummary(projectId, projects.Rows.Single(x => x.Project.ProjectId == projectId).Project.Status, "我方 v2", "对方 v2", null, 0));
        public Task<VersionRestoreState> RestoreStateAsync(Guid versionId, CancellationToken token = default) => Task.FromResult(new VersionRestoreState("无 Working Copy", null, null, 0));
        public Task SetStatusAsync(Guid projectId, ProjectStatus status, CancellationToken token = default)
        { var index = projects.Rows.FindIndex(x => x.Project.ProjectId == projectId); projects.Rows[index] = projects.Rows[index] with { Project = projects.Rows[index].Project with { Status = status } }; return Task.CompletedTask; }
        public Task<ProjectDeletionRecord> DeleteAsync(Guid projectId, bool confirmed, CancellationToken token = default)
        { if (!confirmed) throw new ArgumentException("需要确认"); Deleted = true; projects.Rows.RemoveAll(x => x.Project.ProjectId == projectId); return Task.FromResult(new ProjectDeletionRecord(1, Guid.NewGuid(), projectId, [], [], [], DateTimeOffset.UtcNow, "Completed", "外部 DOCX 保留。")); }
    }
    [Fact] public async Task ArchiveRecycleRestoreAndTwoStepPermanentConfirmationUseStableProjectIdentity()
    {
        var projects = new ProjectsTests.Service(); var lifecycle = new Lifecycle(projects); var vm = new ProjectsViewModel(projects, lifecycle: lifecycle) { ProjectName = "测试项目" }; await vm.SaveCommand.ExecuteAsync(null); await vm.SelectProjectAsync(projects.Rows[0]);
        await vm.ArchiveCommand.ExecuteAsync(null); Assert.Empty(vm.Projects); vm.StatusFilter = "归档"; await vm.SearchCommand.ExecuteAsync(null); Assert.Single(vm.Projects);
        await vm.RestoreCommand.ExecuteAsync(null); vm.StatusFilter = "活跃项目"; await vm.SearchCommand.ExecuteAsync(null); Assert.Single(vm.Projects);
        await vm.ConfirmPermanentDeleteCommand.ExecuteAsync(null); Assert.False(lifecycle.Deleted);
        await vm.RecycleCommand.ExecuteAsync(null); await vm.RequestPermanentDeleteCommand.ExecuteAsync(null); Assert.True(vm.PermanentPrompt); Assert.Contains("测试项目", vm.PermanentWarning); Assert.False(lifecycle.Deleted);
        vm.CancelPermanentDeleteCommand.Execute(null); await vm.ConfirmPermanentDeleteCommand.ExecuteAsync(null); Assert.False(lifecycle.Deleted);
        await vm.RequestPermanentDeleteCommand.ExecuteAsync(null); await vm.ConfirmPermanentDeleteCommand.ExecuteAsync(null); Assert.True(lifecycle.Deleted); Assert.Empty(projects.Rows); Assert.Contains("DOCX 保留", vm.Message);
    }
    [Fact] public async Task ProjectStateAndArchiveNavigationRemainVisibleWithoutFakeSettings()
    {
        var projects = new ProjectsTests.Service(); var lifecycle = new Lifecycle(projects); var id = Guid.NewGuid(); await projects.SaveAsync(id, new("项目", "用户对方", "类型", null, null, "", [], ""));
        var versions = new VersionManagementTests.Service(); var workspace = new VersionManagementViewModel(versions, lifecycle: lifecycle); await workspace.OpenProjectAsync(id); Assert.Contains("我方 v2", workspace.SummaryText); Assert.Contains("Pending Restore", workspace.SummaryText); Assert.True(workspace.CanImport);
        await lifecycle.SetStatusAsync(id, ProjectStatus.Archived); await workspace.RefreshCommand.ExecuteAsync(null); Assert.False(workspace.CanImport); Assert.True(workspace.CanEdit);
        var vm = new ProjectsViewModel(projects, versions: workspace, lifecycle: lifecycle); var main = new MainViewModel(new ComparisonSetupViewModel(), projects: vm); main.NavigateCommand.Execute("archive"); Assert.True(main.IsProjects); Assert.Equal("归档", vm.StatusFilter); main.NavigateCommand.Execute("recycle"); Assert.Equal("回收站", vm.StatusFilter);
    }
}
