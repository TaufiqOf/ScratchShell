using System.Windows.Media;

namespace ScratchShell.UserControls.TerminalControl;

public class AnsiStyle
{
    public Brush Foreground { get; set; } = Brushes.White;
    public Brush Background { get; set; } = null;
    public FontWeight FontWeight { get; set; } = FontWeights.Normal;
    public FontStyle FontStyle { get; set; } = FontStyles.Normal;
    public TextDecorationCollection TextDecorations { get; set; } = new TextDecorationCollection();

    public void Reset()
    {
        Foreground = Brushes.White;
        Background = null;
        FontWeight = FontWeights.Normal;
        FontStyle = FontStyles.Normal;
        TextDecorations = new TextDecorationCollection();
    }

    public AnsiStyle Clone()
    {
        return new AnsiStyle
        {
            Foreground = Foreground,
            Background = Background,
            FontWeight = FontWeight,
            FontStyle = FontStyle,
            TextDecorations = new TextDecorationCollection(TextDecorations)
        };
    }
}
