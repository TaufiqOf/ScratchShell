using System.Text.RegularExpressions;

namespace ScratchShell.UserControls.TerminalControl.Handler;

/// <summary>
/// Handles DEC private mode set/reset sequences (e.g. ESC[?25h, ESC[?7l, ESC[?1049h).
/// </summary>
public class ModeCommandHandler : IAnsiCommandHandler
{
    public void Handle(string sequence, TerminalState state)
    {
        var match = Regex.Match(sequence, @"\x1B\[\?(?<code>[0-9]+)(?<mode>[hl])");
        if (!match.Success) return;

        string code = match.Groups["code"].Value;
        string mode = match.Groups["mode"].Value;

        bool enable = mode == "h";

        switch (code)
        {
            case "1":
                // Application cursor keys
                state.ApplicationCursorKeys = enable;
                break;

            case "3":
                // 132-column mode (ignored by most modern terminals)
                state.Columns132Mode = enable;
                break;

            case "5":
                // Reverse video (light/dark background toggle)
                state.ReverseVideo = enable;
                break;

            case "6":
                // Origin mode (relative/absolute cursor positioning)
                state.OriginMode = enable;
                break;

            case "7":
                // Line wrapping
                state.LineWrap = enable;
                break;

            case "12":
                // Blinking cursor (may affect style)
                state.BlinkingCursor = enable;
                break;

            case "25":
                // Show/hide cursor
                state.CursorVisible = enable;
                break;

            case "47":
                // Use alternate screen buffer (legacy)
                state.AlternateScreenBuffer = enable;
                break;

            case "1000":
                // Enable mouse tracking (normal mode)
                state.MouseTrackingEnabled = enable;
                break;

            case "1001":
                // Highlight mouse tracking (not widely supported)
                state.MouseHighlightTracking = enable;
                break;

            case "1002":
                // Mouse button event tracking
                state.MouseButtonTracking = enable;
                break;

            case "1003":
                // Mouse all-motion tracking
                state.MouseMotionTracking = enable;
                break;

            case "1004":
                // Focus in/out events
                state.FocusEventTracking = enable;
                break;

            case "1047":
                // Alternate screen buffer (similar to 47)
                state.AlternateScreenBuffer = enable;
                break;

            case "1048":
                // Save/restore cursor (for alternate screen mode)
                if (enable)
                    state.SaveCursor();
                else
                    state.RestoreCursor();
                break;

            case "1049":
                // Save cursor + alternate screen buffer
                if (enable)
                {
                    state.SaveCursor();
                    state.AlternateScreenBuffer = true;
                }
                else
                {
                    state.AlternateScreenBuffer = false;
                    state.RestoreCursor();
                }
                break;

            default:
                // Unhandled mode code
                //state.UnhandledModes.Add(code, enable);
                break;
        }
    }
}