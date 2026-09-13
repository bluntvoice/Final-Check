using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

public partial class ComparisonPreviewView : UserControl
{
    private ComparisonPreviewViewModel? model;
    private bool activeBaseline = true;
    private bool syncing;
    public ComparisonPreviewView()
    {
        InitializeComponent(); Attach(BaselineList, true); Attach(CurrentList, false);
        DataContextChanged += (_, _) => Rebind();
        DetachedFromVisualTree += (_, _) => { if (model is not null) model.ScrollRequested -= Scroll; model = null; };
        AttachedToVisualTree += (_, _) => Rebind();
    }
    private void Rebind()
    {
        if (model is not null) model.ScrollRequested -= Scroll;
        model = DataContext as ComparisonPreviewViewModel;
        if (model is null) return;
        model.ScrollRequested += Scroll;
        // SelectedItem already carries the initial change location; realize it after the template loads.
        Dispatcher.UIThread.Post(() =>
        {
            if (model?.LocatedBaseline is { } left) BaselineList.ScrollIntoView(left);
            if (model?.LocatedCurrent is { } right) CurrentList.ScrollIntoView(right);
        }, DispatcherPriority.Loaded);
    }
    private void Attach(ListBox list, bool baseline)
    {
        list.AddHandler(InputElement.PointerPressedEvent, (_, _) => activeBaseline = baseline, RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => activeBaseline = baseline, RoutingStrategies.Tunnel);
        list.GotFocus += (_, _) => activeBaseline = baseline;
        list.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) =>
        {
            if (syncing || !IsVisible || activeBaseline != baseline || model is null) return;
            // Identify an actually visible realized logical row. Do not estimate with offset/extent ratios.
            var first = list.GetRealizedContainers().Select(c => (Container: c, Point: c.TranslatePoint(default, list)))
                .Where(c => c.Point is { } p && p.Y + c.Container.Bounds.Height > 1 && p.Y < list.Bounds.Height)
                .OrderBy(c => c.Point!.Value.Y).FirstOrDefault();
            if (first.Container is not null) model.ViewportMoved(baseline, list.IndexFromContainer(first.Container));
        }, RoutingStrategies.Bubble);
    }
    private void Scroll(bool baseline, int index)
    {
        syncing = true; var list = baseline ? BaselineList : CurrentList;
        list.ScrollIntoView(index);
        Dispatcher.UIThread.Post(() => syncing = false, DispatcherPriority.Background);
    }
}
