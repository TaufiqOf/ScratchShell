namespace ScratchShell.UserControls.TerminalControl.Handler;

public class AsciiControlHandler : IAnsiCommandHandler
{
    public void Handle(string sequence, TerminalState state)
    {
        foreach (char c in sequence)
        {
            switch (c)
            {
                case '\a': Console.Beep(); break;         // Bell
                case '\b': state.CursorCol = Math.Max(0, state.CursorCol - 1); break; // Backspace
                case '\n': state.CursorRow++; state.CursorCol = 0; break;  // Newline
                case '\r': state.CursorCol = 0; break;     // Carriage return
                                                           // Add more as needed
            }
        }
    }
}