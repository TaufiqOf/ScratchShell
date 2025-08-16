namespace ScratchShell.UserControls.TerminalControl;

public class AnsiSegment
{
    public string Text { get; set; }
    public AnsiStyle Style { get; set; }

    public AnsiSegment(string text, AnsiStyle style)
    {
        Text = text;
        Style = style.Clone(); // Ensure immutability between segments
    }
}
