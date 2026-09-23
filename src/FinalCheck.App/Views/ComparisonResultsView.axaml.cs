using Avalonia.Controls;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

public partial class ComparisonResultsView : UserControl
{
    private bool? compact;
    public ComparisonResultsView()
    {
        InitializeComponent(); SizeChanged += (_, _) => ArrangeWorkspace();
    }
    private void ArrangeWorkspace()
    {
        var narrow = Bounds.Width < 1200;
        if (compact == narrow) return;
        compact = narrow;
        // Details remain visible alongside/under the document area; neither layout requires a tab switch.
        Workspace.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,12,290");
        Workspace.RowDefinitions = new RowDefinitions(narrow ? "3*,8,2*" : "*");
        Grid.SetColumn(DetailArea, narrow ? 0 : 2); Grid.SetRow(DetailArea, narrow ? 2 : 0);
    }
    private void SelectMember(object? sender, SelectionChangedEventArgs e)
    {
        // Replacing the group ItemsSource can transiently clear ListBox.SelectedItem.
        // Only a real non-null member selection may change the shared workspace selection.
        if (sender is ListBox { SelectedItem: ChangeItemViewModel member } && DataContext is ComparisonResultsViewModel model
            && model.SelectedEntry?.Members.Contains(member) == true)
            model.SelectedChange = member;
    }
}
