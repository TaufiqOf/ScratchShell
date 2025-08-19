using ScratchShell.UserControls.TerminalControl;

namespace ScratchShell.UserControls
{
    public interface ITerminal
    {
        public delegate void TerminalCommandHandler(ITerminal obj, string command);
        public delegate void TerminalSizeHandler(ITerminal obj, Size size);
        public event TerminalCommandHandler CommandEntered;
        public event TerminalSizeHandler TerminalSizeChanged;
        string InputLineSyntax { get; set; }
        bool IsReadOnly { get; set; }
        string Text { get; }

        void AddOutput(string v);
    }
}