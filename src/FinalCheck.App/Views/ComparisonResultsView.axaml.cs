using Avalonia.Controls;
using Avalonia;
using System.ComponentModel;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

public partial class ComparisonResultsView : UserControl
{
    private (bool StackedContext, bool DetailsBelow)? layout;
    private ComparisonResultsViewModel? model;
    public ComparisonResultsView()
    {
        InitializeComponent(); SizeChanged += (_, _) => ArrangeWorkspace();
        DataContextChanged += (_, _) => Rebind();
        ContextBaselineArea.DataContextChanged += (_, _) => ContextBaselineArea.Offset = new Vector();
        ContextCurrentArea.DataContextChanged += (_, _) => ContextCurrentArea.Offset = new Vector();
    }
    private void Rebind()
    {
        if (model is not null) model.PropertyChanged -= ModelChanged;
        model = DataContext as ComparisonResultsViewModel;
        if (model is not null) model.PropertyChanged += ModelChanged;
        layout = null;
        ArrangeWorkspace();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComparisonResultsViewModel.FullDocumentMode)) ArrangeWorkspace();
    }
    private void ArrangeWorkspace()
    {
        var stackedContext = Bounds.Width < 1200;
        var detailsBelow = Bounds.Width < 800 || model?.FullDocumentMode == true && Bounds.Width < 1200;
        if (layout == (stackedContext, detailsBelow)) return;
        layout = (stackedContext, detailsBelow);
        // Ordinary desktop widths keep actions beside the contexts; only truly narrow windows stack details below.
        Workspace.ColumnDefinitions = new ColumnDefinitions(detailsBelow ? "*" : $"*,12,{(stackedContext ? 260 : 290)}");
        Workspace.RowDefinitions = new RowDefinitions(detailsBelow ? "3*,8,2*" : "*");
        Grid.SetColumn(DetailArea, detailsBelow ? 0 : 2); Grid.SetRow(DetailArea, detailsBelow ? 2 : 0);
        ContextWorkspace.ColumnDefinitions = new ColumnDefinitions(stackedContext ? "*" : "*,8,*");
        ContextWorkspace.RowDefinitions = new RowDefinitions(stackedContext ? "*,8,*" : "*");
        Grid.SetColumn(ContextCurrentArea, stackedContext ? 0 : 2); Grid.SetRow(ContextCurrentArea, stackedContext ? 2 : 0);
    }
}
