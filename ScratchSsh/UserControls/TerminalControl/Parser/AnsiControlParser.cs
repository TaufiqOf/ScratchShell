using ScratchShell.UserControls.TerminalControl.Handler;
using ScratchShell.UserControls.TerminalControl.Handler;
using ScratchShell.UserControls.TerminalControl.Parser;
using System.Text.RegularExpressions;

namespace ScratchShell.UserControls.TerminalControl.Parser;

public class AnsiControlParser
{
    private readonly List<IAnsiCommandHandler> _handlers;

    public AnsiControlParser()
    {
        _handlers = new List<IAnsiCommandHandler>
        {
            new AsciiControlHandler(),
            new CursorCommandHandler(),
            new EraseCommandHandler(),
            new ModeCommandHandler(),
            new OscCommandHandler()
        };
    }

    public void ParseAndHandle(string input, TerminalState state)
    {
        int i = 0;
        while (i < input.Length)
        {
            if (input[i] != '\x1B') { i++; continue; }

            if (i + 1 < input.Length)
            {
                char next = input[i + 1];

                // CSI sequence
                if (next == '[')
                {
                    var match = Regex.Match(input.Substring(i), @"\x1B\[(?<code>[0-9;?]*)(?<command>[A-Za-z])");
                    if (match.Success)
                    {
                        string sequence = match.Value;
                        foreach (var handler in _handlers)
                            handler.Handle(sequence, state);

                        i += match.Length;
                        continue;
                    }
                }
                // OSC (Operating System Command) - ESC ] ... BEL or ESC \
                else if (next == ']')
                {
                    int end = input.IndexOf('\a', i); // BEL terminated
                    if (end == -1)
                        end = input.IndexOf("\x1B\\", i); // ESC \ terminated

                    if (end != -1)
                    {
                        string sequence = input.Substring(i, end - i + 1);
                        foreach (var handler in _handlers)
                            handler.Handle(sequence, state);
                        i = end + 1;
                        continue;
                    }
                }
                // DCS, ESCP...ESC\
                else if (next == 'P')
                {
                    int end = input.IndexOf("\x1B\\", i);
                    if (end != -1)
                    {
                        string sequence = input.Substring(i, end - i + 2);
                        foreach (var handler in _handlers)
                            handler.Handle(sequence, state);
                        i = end + 2;
                        continue;
                    }
                }
                // 2-character sequences like ESCc, ESC7, ESC8, ESCH
                else
                {
                    if (i + 2 <= input.Length)
                    {
                        string sequence = input.Substring(i, 2);
                        foreach (var handler in _handlers)
                            handler.Handle(sequence, state);
                        i += 2;
                        continue;
                    }
                }
            }

            i++; // fallback
        }
    }
}