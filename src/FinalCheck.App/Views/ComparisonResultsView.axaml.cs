using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Primitives;
using System.ComponentModel;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

public partial class ComparisonResultsView : UserControl
{
    private (bool StackedContext, bool DetailsBelow, bool ShortWindow)? layout;
    private ComparisonResultsViewModel? model;
    public ComparisonResultsView()
    {
        InitializeComponent(); SizeChanged += (_, _) => ArrangeWorkspace();
        WorkspaceViewport.SizeChanged += (_, _) => ArrangeWorkspace();
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
        if (e.PropertyName == nameof(ComparisonResultsViewModel.FullDocumentMode))
        {
            WorkspaceViewport.Offset = new Vector();
            ArrangeWorkspace();
        }
        else if (e.PropertyName == nameof(ComparisonResultsViewModel.SelectedChange) && model?.ContextMode == true)
            WorkspaceViewport.Offset = new Vector();
    }
    private void ArrangeWorkspace()
    {
        var stackedContext = Bounds.Width < 1200;
        var detailsBelow = Bounds.Width < 800 || model?.FullDocumentMode == true && Bounds.Width < 1200;
        var shortWindow = Bounds.Height < 540;
        // Keep a finite content height: an unconstrained star grid would collapse its context rows
        // or disable the full document's virtualization inside the outer scroll viewer.
        Workspace.Height = detailsBelow ? Math.Max(560, WorkspaceViewport.Bounds.Height) : double.NaN;
        if (layout == (stackedContext, detailsBelow, shortWindow)) return;
        layout = (stackedContext, detailsBelow, shortWindow);
        SummaryViewport.MaxHeight = shortWindow ? 150 : double.PositiveInfinity;
        WorkspaceViewport.VerticalScrollBarVisibility = detailsBelow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        // Ordinary desktop widths keep actions beside the contexts; only truly narrow windows stack details below.
        Workspace.ColumnDefinitions = new ColumnDefinitions(detailsBelow ? "*" : $"*,12,{(stackedContext ? 260 : 290)}");
        Workspace.RowDefinitions = new RowDefinitions(detailsBelow ? "3*,8,2*" : "*");
        Grid.SetColumn(DetailArea, detailsBelow ? 0 : 2); Grid.SetRow(DetailArea, detailsBelow ? 2 : 0);
        ContextWorkspace.ColumnDefinitions = new ColumnDefinitions(stackedContext ? "*" : "*,8,*");
        ContextWorkspace.RowDefinitions = new RowDefinitions(stackedContext ? "*,8,*" : "*");
        Grid.SetColumn(ContextCurrentArea, stackedContext ? 0 : 2); Grid.SetRow(ContextCurrentArea, stackedContext ? 2 : 0);
    }
}
