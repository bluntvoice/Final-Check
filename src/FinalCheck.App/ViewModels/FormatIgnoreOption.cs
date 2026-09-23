using CommunityToolkit.Mvvm.ComponentModel;

namespace FinalCheck.App.ViewModels;

public sealed partial class FormatIgnoreOption(string label, params string[] keys) : ObservableObject
{
    public string Label { get; } = label;
    public IReadOnlyList<string> Keys { get; } = keys;
    [ObservableProperty] private bool isSelected;

    public static IReadOnlyList<FormatIgnoreOption> Supported =>
    [
        new("字体（中/英/西文/复杂文字）", "Character.Font.Ascii", "Character.Font.HighAnsi", "Character.Font.EastAsia", "Character.Font.ComplexScript"),
        new("字号", "Character.FontSizeHalfPoints"), new("字体颜色", "Character.Color"),
        new("加粗", "Character.Bold"), new("斜体", "Character.Italic"), new("下划线", "Character.Underline"),
        new("删除线", "Character.Strike"), new("高亮", "Character.Highlight"),
        new("段落对齐", "Paragraph.Alignment"), new("左缩进", "Paragraph.LeftIndent"),
        new("右缩进", "Paragraph.RightIndent"), new("首行缩进", "Paragraph.FirstLineIndent"),
        new("悬挂缩进", "Paragraph.HangingIndent"), new("段前间距", "Paragraph.SpacingBefore"),
        new("段后间距", "Paragraph.SpacingAfter"), new("行间距 / 行距规则", "Paragraph.LineSpacing", "Paragraph.LineRule"),
        new("表格/单元格对齐", "Table.Alignment", "TableCell.VerticalAlignment"),
        new("表格/单元格边框", "Table.Borders", "TableCell.Borders"),
        new("表格/单元格底色", "Table.ShadingFill", "TableCell.ShadingFill"),
        new("表格/单元格宽度", "Table.Width", "Table.WidthType", "TableCell.Width", "TableCell.WidthType"),
        new("表格样式", "Table.StyleId"),
    ];
}
