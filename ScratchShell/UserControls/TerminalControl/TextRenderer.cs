using System.Windows.Documents;

namespace ScratchShell.UserControls.TerminalControl;

public class TextRenderer
{
    public IEnumerable<Run> Render(IEnumerable<AnsiSegment> segments)
    {
        foreach (var segment in segments)
        {
            var run = new Run(segment.Text)
            {
                Foreground = segment.Style.Foreground,
                FontWeight = segment.Style.FontWeight,
                FontStyle = segment.Style.FontStyle,
                TextDecorations = segment.Style.TextDecorations
            };

            if (segment.Style.Background != null)
                run.Background = segment.Style.Background;

            yield return run;
        }
    }
}

