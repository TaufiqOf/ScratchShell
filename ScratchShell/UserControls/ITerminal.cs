using ScratchShell.UserControls.ThemeControl;

namespace ScratchShell.UserControls;

public interface ITerminal
{
    public delegate void TerminalCommandHandler(ITerminal obj, string command);

    public delegate void TerminalSizeHandler(ITerminal obj, Size size);

    public event TerminalCommandHandler CommandEntered;

    public event TerminalSizeHandler TerminalSizeChanged;

    string InputLineSyntax { get; set; }
    bool IsReadOnly { get; set; }
    string Text { get; }
    double Width { get; }
    double Height { get; }

    void AddOutput(string output);

    void AddInput(string input);

    TerminalTheme Theme { get; set; }

    void RefreshTheme();

    // Copy/Paste functionality
    bool HasSelection();

    void CopySelection();

    void PasteFromClipboard();

    void PasteText(string text);

    void SelectAll();
}