using FinalCheck.App.ViewModels;
using FinalCheck.Core.Management;

namespace FinalCheck.App.Tests;

public sealed class ProjectsTests
{
    internal sealed class Service : IProjectService
    {
        public ProjectQuery? Query { get; private set; }
        public List<ProjectListItem> Rows { get; } = [];
        public Task<IReadOnlyList<ProjectListItem>> ListAsync(ProjectQuery query, CancellationToken token = default) { Query = query; return Task.FromResult<IReadOnlyList<ProjectListItem>>(Rows.Where(r => r.Project.ProjectName.Contains(query.Search) && r.Project.Status == query.Status).Skip(query.Offset).Take(query.Limit).ToArray()); }
        public Task<ContractProject> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult(Rows.Single(r => r.Project.ProjectId == id).Project);
        public Task<IReadOnlyList<ProjectFolder>> FoldersAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<ProjectFolder>>([]);
        public Task<ContractProject> SaveAsync(Guid? id, ProjectEdit edit, CancellationToken token = default)
        {
            var project = new ContractProject(id ?? Guid.NewGuid(), edit.ProjectName, edit.Counterparty, edit.ContractType, edit.TemplateId, edit.TemplateVersionId, null, edit.Tags, ProjectStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, edit.Notes, null);
            Rows.RemoveAll(r => r.Project.ProjectId == project.ProjectId); Rows.Add(new(project, "模板", "1.0.0", edit.FolderName)); return Task.FromResult(project);
        }
    }
    [Fact] public async Task TemplateSelectionSuggestsTypeButDoesNotInferCounterpartyOrAutoSave()
    {
        var service = new Service(); var templates = new TemplateCenterTests.Service(); var vm = new ProjectsViewModel(service, templates); await vm.RefreshCommand.ExecuteAsync(null);
        vm.BoundTemplate = vm.Templates[0]; Assert.Equal("代理", vm.ContractType); Assert.Equal("", vm.Counterparty); Assert.Empty(service.Rows);
        vm.Counterparty = "用户填写对方"; vm.SuggestNameCommand.Execute(null); Assert.Contains("用户填写对方", vm.ProjectName);
        await vm.SaveCommand.ExecuteAsync(null); Assert.Single(service.Rows); Assert.Equal(templates.Detail.Current!.TemplateVersionId, service.Rows[0].Project.BoundTemplateVersionId);
    }
    [Fact] public async Task SearchRefreshDoesNotTurnEditingExistingProjectIntoAccidentalNewProject()
    {
        var service = new Service(); var vm = new ProjectsViewModel(service) { ProjectName = "项目" }; await vm.SaveCommand.ExecuteAsync(null); var id = service.Rows[0].Project.ProjectId;
        await vm.SelectProjectAsync(service.Rows[0]); vm.SearchText = "不匹配"; await vm.SearchCommand.ExecuteAsync(null); Assert.Empty(vm.Projects);
        vm.ProjectName = "改名"; await vm.SaveCommand.ExecuteAsync(null); Assert.Single(service.Rows); Assert.Equal(id, service.Rows[0].Project.ProjectId);
        vm.NewProjectCommand.Execute(null); vm.ProjectName = "第二项目"; await vm.SaveCommand.ExecuteAsync(null); Assert.Equal(2, service.Rows.Count);
    }
    [Fact] public async Task FiltersAndTagColorValidationStayExplicit()
    {
        var service = new Service(); var vm = new ProjectsViewModel(service) { SearchText = "名称", FilterType = "代理", TimeFilter = "最近 7 天", FilterTag = "重要" };
        await vm.SearchCommand.ExecuteAsync(null); Assert.Equal("名称", service.Query!.Search); Assert.Equal("代理", service.Query.ContractType); Assert.NotNull(service.Query.UpdatedSince);
        vm.TagName = "标签"; vm.TagColor = "invalid"; vm.AddTagCommand.Execute(null); Assert.Empty(vm.Tags);
        vm.TagColor = "#2874a6"; vm.AddTagCommand.Execute(null); Assert.Equal("#2874A6", vm.Tags[0].Color);
        vm.RemoveTagCommand.Execute(vm.Tags[0]); Assert.Empty(vm.Tags); vm.ClearFiltersCommand.Execute(null); Assert.Equal("", vm.SearchText);
    }
    [Fact] public async Task RefreshPreservesSelectedTemplateAndBindingVersionForExistingEdit()
    {
        var service = new Service(); var templates = new TemplateCenterTests.Service(); var vm = new ProjectsViewModel(service, templates); await vm.RefreshCommand.ExecuteAsync(null);
        vm.ProjectName = "项目"; vm.BoundTemplate = vm.Templates[0]; await vm.SaveCommand.ExecuteAsync(null); await vm.SelectProjectAsync(service.Rows[0]);
        await vm.RefreshCommand.ExecuteAsync(null); await vm.SaveCommand.ExecuteAsync(null); Assert.Single(service.Rows); Assert.Equal(templates.Detail.Current!.TemplateVersionId, service.Rows[0].Project.BoundTemplateVersionId);
    }
}
