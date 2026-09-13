using Avalonia.Controls;
using FinalCheck.App.ViewModels;
using FinalCheck.Core.Management;

namespace FinalCheck.App.Views;
public partial class ProjectsView : UserControl
{
    public ProjectsView() => InitializeComponent();
    private async void SelectProject(object? sender, SelectionChangedEventArgs e)
    { if (DataContext is ProjectsViewModel vm && e.AddedItems.OfType<ProjectListItem>().FirstOrDefault() is { } item) await vm.SelectProjectAsync(item); }
}
