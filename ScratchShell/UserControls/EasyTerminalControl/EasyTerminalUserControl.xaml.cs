using EasyWindowsTerminalControl;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScratchShell.UserControls.EasyTerminalControl;

/// <summary>
/// Interaction logic for EasyTerminalUserControl.xaml
/// </summary>
public partial class EasyTerminalUserControl : UserControl, ITerminal
{
    private const string USE_DELIMITER = "__NOTINMYFILES__";

    public EasyTerminalUserControl()
    {
        InitializeComponent();
        Terminal.ConPTYTerm = new ReadDelimitedTermPTY(delimiter: USE_DELIMITER);
    }

    private bool _isReadOnly;
    public string InputLineSyntax { get; set; }

    public bool IsReadOnly
    {
        get
        {
            return _isReadOnly;
        }
        set
        {
            _isReadOnly = value;
            if (value)
            {
                Terminal.IsReadOnly = true;
                Terminal.Background = Brushes.LightGray;
            }
            else
            {
                Terminal.IsReadOnly = false;
                Terminal.Background = Brushes.White;
            }
        }
    }

    public string Text => Terminal.ConPTYTerm.GetConsoleText();

    public event ITerminal.TerminalCommandHandler CommandEntered;

    public event ITerminal.TerminalSizeHandler TerminalSizeChanged;

    public void AddInput(string input)
    {
        Terminal.ConPTYTerm.WriteToTerm(input);
    }

    public void AddOutput(string output)
    {
        Terminal.ConPTYTerm.WriteToTerm(output);
    }

    public TerminalTheme Theme { get; set; } = new TerminalTheme();
    public void RefreshTheme() { /* No-op or implement if needed */ }
}