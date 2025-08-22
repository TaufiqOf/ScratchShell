using System.Windows.Media;

public class TerminalTheme
{
    public FontFamily FontFamily { get; set; } = new FontFamily("Consolas");
    public double FontSize { get; set; } = 16.0;
    public Brush Foreground { get; set; } = Brushes.LightGray;
    public Brush Background { get; set; } = Brushes.Black;
    public Color SelectionColor { get; set; } = Color.FromArgb(80, 0, 120, 255);
    public Brush CursorColor { get; set; } = Brushes.LightGray;
    public Color CopySelectionColor { get; set; } = Color.FromArgb(180, 144, 238, 144); // LightGreen
}
