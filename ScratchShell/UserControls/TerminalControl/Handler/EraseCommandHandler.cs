using System.Text.RegularExpressions;

namespace ScratchShell.UserControls.TerminalControl.Handler;

public class EraseCommandHandler : IAnsiCommandHandler
{
    public void Handle(string sequence, TerminalState state)
    {
        if (Regex.IsMatch(sequence, @"\x1B\[[0-9]*J")) // Clear screen
        {
            state.ClearScreen();
        }
        else if (Regex.IsMatch(sequence, @"\x1B\[[0-9]*K")) // Clear line
        {
            state.ClearLine();
        }
    }
}