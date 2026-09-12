using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

public sealed class DiffTextBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<DiffSegment>?> SegmentsProperty = AvaloniaProperty.Register<DiffTextBlock, IReadOnlyList<DiffSegment>?>(nameof(Segments));
    public static readonly StyledProperty<bool> BaselineProperty = AvaloniaProperty.Register<DiffTextBlock, bool>(nameof(Baseline));
    public IReadOnlyList<DiffSegment>? Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public bool Baseline { get => GetValue(BaselineProperty); set => SetValue(BaselineProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SegmentsProperty && change.Property != BaselineProperty) return;
        Inlines ??= []; Inlines.Clear();
        foreach (var segment in Segments ?? []) Inlines.Add(new Run(segment.Text)
        { Background = segment.Changed ? Brush.Parse(Baseline ? "#FFE3E3" : "#DDF4E8") : Brushes.Transparent,
          Foreground = segment.Changed ? Brush.Parse(Baseline ? "#A12020" : "#11663F") : Brushes.Black,
          FontWeight = segment.Changed ? FontWeight.SemiBold : FontWeight.Normal });
    }
}
