using Avalonia.Controls;

namespace FinalCheck.App.Views;

public partial class FormatRestoreView : UserControl
{
    public FormatRestoreView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RestoreHeader.MaxHeight = Math.Clamp(Bounds.Height * 0.45, 60, 190);
    }
}
