using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Views;

/// <summary>Creates native content only for realized virtualized rows. Never opens a source DOCX.</summary>
public sealed class SnapshotPreviewBlock : ContentControl
{
    public static readonly StyledProperty<PreviewBlock?> BlockProperty = AvaloniaProperty.Register<SnapshotPreviewBlock, PreviewBlock?>(nameof(Block));
    public static readonly StyledProperty<bool> BaselineProperty = AvaloniaProperty.Register<SnapshotPreviewBlock, bool>(nameof(Baseline));
    public static readonly StyledProperty<string?> TargetNodeIdProperty = AvaloniaProperty.Register<SnapshotPreviewBlock, string?>(nameof(TargetNodeId));
    public PreviewBlock? Block { get => GetValue(BlockProperty); set => SetValue(BlockProperty, value); }
    public bool Baseline { get => GetValue(BaselineProperty); set => SetValue(BaselineProperty, value); }
    public string? TargetNodeId { get => GetValue(TargetNodeIdProperty); set => SetValue(TargetNodeIdProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != BlockProperty && change.Property != BaselineProperty && change.Property != TargetNodeIdProperty) return;
        if (Block is not { } block) { Content = null; return; }
        var outer = new StackPanel { Spacing = 4, Margin = new Thickness(6) };
        outer.Children.Add(new TextBlock { Text = block.Label, FontSize = 11, Foreground = Brush.Parse("#64748B") });
        var grid = new Grid(); var width = block.Cells.Count == 0 ? 1 : block.Cells.Max(c => (long)c.Column + c.Span);
        if (width > 64 || block.Cells.Any(c => c.Column < 0))
        {
            outer.Children.Add(new TextBlock { Text = "复杂表格行以文本展示（非原排版）", Foreground = Brush.Parse("#9A5B00"), TextWrapping = TextWrapping.Wrap });
            outer.Children.Add(new TextBlock { Text = string.Join("\n", block.Cells.Select(c => $"第 {c.Column + 1L} 列：" +
                string.Join("\n", c.Paragraphs.Select(p => string.Concat(p.Segments.Select(s => s.Text)))))), TextWrapping = TextWrapping.Wrap });
            Content = outer; return;
        }
        var count = (int)width;
        for (var i = 0; i < count; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        foreach (var cell in block.Cells)
        {
            var paragraphs = new StackPanel { Spacing = 6 };
            foreach (var paragraph in cell.Paragraphs)
            {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 20,
                    TextAlignment = paragraph.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left } };
                text.Inlines ??= [];
                foreach (var segment in paragraph.Segments)
                {
                    var format = segment.Format;
                    var inline = new Run(segment.Text) { FontWeight = format.Bold == true ? FontWeight.Bold : FontWeight.Normal,
                        FontStyle = format.Italic == true ? FontStyle.Italic : FontStyle.Normal,
                        FontSize = Math.Clamp((format.FontSizeHalfPoints ?? 22) * 2d / 3, 6, 96),
                        Foreground = ColorBrush(format.Color), Background = segment.Changed ? Brush.Parse(Baseline ? "#FFE3E3" : "#DDF4E8") : HighlightBrush(format.Highlight) };
                    var font = segment.Text.Any(c => c >= '\u2e80') ? format.Fonts.EastAsia : format.Fonts.Ascii ?? format.Fonts.HighAnsi;
                    if (!string.IsNullOrWhiteSpace(font)) inline.FontFamily = new FontFamily(font);
                    if (format.Underline is not (null or "none")) inline.TextDecorations = TextDecorations.Underline;
                    if (format.Strike == true) inline.TextDecorations = new TextDecorationCollection((inline.TextDecorations ?? []).Concat(TextDecorations.Strikethrough));
                    text.Inlines.Add(inline);
                }
                paragraphs.Children.Add(text);
            }
            var border = new Border { Child = paragraphs, Padding = new Thickness(block.IsTable ? 8 : 0),
                Background = TargetNodeId is not null && (cell.NodeId == TargetNodeId || cell.Paragraphs.Any(p => p.NodeIds.Contains(TargetNodeId, StringComparer.Ordinal))) ? Brush.Parse("#FFF3CC") : Brushes.Transparent,
                BorderThickness = new Thickness(block.IsTable ? 1 : 0), BorderBrush = Brush.Parse("#CBD5E1") };
            Grid.SetColumn(border, cell.Column); Grid.SetColumnSpan(border, cell.Span); grid.Children.Add(border);
        }
        outer.Children.Add(grid); Content = outer;
    }
    private static IBrush ColorBrush(string? color) => color is { Length: 6 } && uint.TryParse(color, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value)
        ? new SolidColorBrush(Color.FromUInt32(0xff000000 | value)) : Brushes.Black;
    private static IBrush HighlightBrush(string? name)
    {
        try { return name is null or "none" ? Brushes.Transparent : Brush.Parse(name); }
        catch (FormatException) { return Brushes.Transparent; }
    }
}
