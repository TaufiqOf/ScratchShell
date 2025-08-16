using System.Text.RegularExpressions;

namespace ScratchShell.UserControls.TerminalControl.Handler;

public class CursorCommandHandler : IAnsiCommandHandler
{
    public void Handle(string sequence, TerminalState state)
    {
        var match = Regex.Match(sequence, @"\x1B\[(?<pos>[0-9;]*)(?<cmd>[ABCDHf])");
        if (!match.Success) return;

        var cmd = match.Groups["cmd"].Value;
        var pos = match.Groups["pos"].Value.Split(';');

        switch (cmd)
        {
            case "A": // Cursor up
                int up = int.TryParse(pos[0], out var a) ? a : 1;
                state.CursorRow = Math.Max(0, state.CursorRow - up);
                break;
            case "B": // Cursor down
                int down = int.TryParse(pos[0], out var b) ? b : 1;
                state.CursorRow += down;
                break;
            case "C": // Right
                int right = int.TryParse(pos[0], out var c) ? c : 1;
                state.CursorCol += right;
                break;
            case "D": // Left
                int left = int.TryParse(pos[0], out var d) ? d : 1;
                state.CursorCol = Math.Max(0, state.CursorCol - left);
                break;
            case "H":
            case "f": // Move to row/col
                int row = pos.Length > 0 && int.TryParse(pos[0], out var r) ? r - 1 : 0;
                int col = pos.Length > 1 && int.TryParse(pos[1], out var l) ? l - 1 : 0;
                state.CursorRow = row;
                state.CursorCol = col;
                break;
        }
    }
}
