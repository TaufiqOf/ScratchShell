using ScratchShell.UserControls.TerminalControl.Handler;

namespace ScratchShell.UserControls.TerminalControl.Handler;

/// <summary>
/// Handles OSC (Operating System Command) ANSI escape sequences.
/// </summary>
public class OscCommandHandler : IAnsiCommandHandler
{
    public void Handle(string sequence, TerminalState state)
    {
        if (!sequence.StartsWith("\x1B]"))
            return;

        // Remove ESC]
        var content = sequence.Substring(2);

        // Determine terminator (BEL or ST)
        string? payload = null;
        if (content.EndsWith("\a")) // BEL
        {
            payload = content[..^1];
        }
        else if (content.EndsWith("\x1B\\")) // ESC \
        {
            payload = content[..^2];
        }

        if (payload == null)
            return;

        var parts = payload.Split(';', 2);
        if (parts.Length < 2)
            return;

        var command = parts[0];
        var data = parts[1];

        switch (command)
        {
            case "0":
                // Set both icon name and window title
                state.IconName = data;
                state.WindowTitle = data;
                break;

            case "1":
                // Set icon name
                state.IconName = data;
                break;

            case "2":
                // Set window title
                state.WindowTitle = data;
                break;

            case "4":
                // Set color palette entry (format: 4;index;rgb:RR/GG/BB)
                HandleColorPalette(data, state);
                break;

            case "10":
                state.ForegroundColor = data;
                break;

            case "11":
                state.BackgroundColor = data;
                break;

            case "12":
                state.CursorColor = data;
                break;

            case "52":
                // Clipboard handling (may be base64 encoded): 52;clipboard;base64...
                HandleClipboard(data, state);
                break;

            case "104":
            case "105":
            case "106":
            case "107":
            case "110":
                // Reset specific color (depends on the terminal)
                state.ResetColor(command);
                break;

            default:
                state.UnhandledOsc[command] = data;
                break;
        }
    }

    private void HandleColorPalette(string data, TerminalState state)
    {
        var parts = data.Split(';');
        if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out int index))
            {
                state.ColorPalette[index] = parts[1]; // e.g., rgb:ff/00/00
            }
        }
    }

    private void HandleClipboard(string data, TerminalState state)
    {
        // Format might be: "c;data" or just "data"
        if (data.StartsWith("c;"))
        {
            state.Clipboard = data.Substring(2);
        }
        else
        {
            state.Clipboard = data;
        }
    }
}
