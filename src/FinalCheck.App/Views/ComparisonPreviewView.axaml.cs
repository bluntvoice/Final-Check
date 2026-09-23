using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

public partial class ComparisonPreviewView : UserControl
{
    private ComparisonPreviewViewModel? model;
    private bool? activeBaseline;
    private readonly DispatcherTimer viewportTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private (bool Baseline, int Index)? pending;
    private int generation;
    public ComparisonPreviewView()
    {
        InitializeComponent(); Attach(BaselineList, true); Attach(CurrentList, false);
        viewportTimer.Tick += (_, _) =>
        {
            viewportTimer.Stop(); var latest = pending; pending = null;
            if (latest is { } anchor && activeBaseline == anchor.Baseline)
                model?.Scrolling.ViewportChanged(Panel(anchor.Baseline), anchor.Index);
        };
        DataContextChanged += (_, _) => Rebind();
        DetachedFromVisualTree += (_, _) => Unbind();
        AttachedToVisualTree += (_, _) => Rebind();
    }
    private void Rebind()
    {
        Unbind();
        model = DataContext as ComparisonPreviewViewModel;
        if (model is null) return;
        model.ScrollRequested += Scroll;
        model.NavigationStarted += ResetInput;
        // SelectedItem already carries the initial change location; realize it after the template loads.
        var binding = model;
        Dispatcher.UIThread.Post(() =>
        {
            if (model != binding) return;
            if (model?.LocatedBaseline is { } left) Scroll(true, BaselineList.Items.IndexOf(left));
            if (model?.LocatedCurrent is { } right) Scroll(false, CurrentList.Items.IndexOf(right));
        }, DispatcherPriority.Loaded);
    }
    private static DocumentPanelId Panel(bool baseline) => baseline ? ComparisonPreviewViewModel.BaselinePanel : ComparisonPreviewViewModel.CurrentPanel;
    private void ResetInput() { activeBaseline = null; pending = null; viewportTimer.Stop(); generation++; }
    private void Unbind()
    {
        ResetInput();
        if (model is not null) { model.ScrollRequested -= Scroll; model.NavigationStarted -= ResetInput; }
        model = null;
    }
    private void UserInput(bool baseline)
    {
        if (activeBaseline != baseline) { pending = null; viewportTimer.Stop(); }
        activeBaseline = baseline; generation++; model?.Scrolling.UserActivated(Panel(baseline));
    }
    private void Attach(ListBox list, bool baseline)
    {
        list.AddHandler(InputElement.PointerPressedEvent, (_, _) => UserInput(baseline), RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => UserInput(baseline), RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        { if (e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Up or Key.Down or Key.Space) UserInput(baseline); }, RoutingStrategies.Tunnel);
        list.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) =>
        {
            if (!IsVisible || activeBaseline != baseline || model is null) return;
            // Anchor the realized row nearest the viewport center, not its first partially visible row.
            var center = list.GetRealizedContainers().Select(c => (Container: c, Point: c.TranslatePoint(default, list)))
                .Where(c => c.Point is { } p && p.Y + c.Container.Bounds.Height > 1 && p.Y < list.Bounds.Height)
                .OrderBy(c => Math.Abs(c.Point!.Value.Y + c.Container.Bounds.Height / 2 - list.Bounds.Height / 2)).FirstOrDefault();
            if (center.Container is null) return;
            pending = (baseline, list.IndexFromContainer(center.Container));
            if (!viewportTimer.IsEnabled) viewportTimer.Start();
        }, RoutingStrategies.Bubble);
    }
    private void Scroll(bool baseline, int index)
    {
        var list = baseline ? BaselineList : CurrentList;
        if (index < 0 || index >= list.ItemCount) return;
        var token = generation; var binding = model;
        list.ScrollIntoView(index);
        Dispatcher.UIThread.Post(() =>
        {
            if (token != generation || model != binding) return;
            if (list.ContainerFromIndex(index) is not { } container || container.TranslatePoint(default, list) is not { } point) return;
            var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroller is null) return;
            // Local pixel adjustment centers the mapped logical block; no cross-document pixel ratio is used.
            var delta = point.Y + container.Bounds.Height / 2 - list.Bounds.Height / 2;
            scroller.Offset = new Vector(scroller.Offset.X, Math.Clamp(scroller.Offset.Y + delta, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height)));
        }, DispatcherPriority.Loaded);
    }
}
