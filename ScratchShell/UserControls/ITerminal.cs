using ScratchShell.UserControls.ThemeControl;
using ScratchShell.Services.Terminal;

namespace ScratchShell.UserControls;

public interface ITerminal
{
    public delegate void TerminalCommandHandler(ITerminal obj, string command);

    public delegate void TerminalSizeHandler(ITerminal obj, Size size);

    public delegate void TabCompletionHandler(ITerminal obj, TabCompletionEventArgs args);

    public event TerminalCommandHandler CommandEntered;

    public event TerminalSizeHandler TerminalSizeChanged;

    public event TabCompletionHandler TabCompletionRequested;

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

    void Focus();

    // AutoComplete functionality
    void ShowAutoCompleteResults(AutoCompleteResult result);

    void HideAutoComplete();

    string GetCurrentInputLine();

    int GetCursorPosition();
}

/// <summary>
/// Event arguments for tab completion requests
/// </summary>
public class TabCompletionEventArgs
{
    public string CurrentLine { get; set; } = string.Empty;
    public int CursorPosition { get; set; }
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool Handled { get; set; }
}