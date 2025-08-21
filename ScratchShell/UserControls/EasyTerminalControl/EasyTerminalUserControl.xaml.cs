using EasyWindowsTerminalControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ScratchShell.UserControls.EasyTerminalControl;
/// <summary>
/// Interaction logic for EasyTerminalUserControl.xaml
/// </summary>
public partial class EasyTerminalUserControl : UserControl,ITerminal
{
    private const string USE_DELIMITER = "__NOTINMYFILES__";
    public EasyTerminalUserControl()
    {
        InitializeComponent();
        Terminal.ConPTYTerm = new ReadDelimitedTermPTY(delimiter: USE_DELIMITER);
    }
    private bool _isReadOnly;
    public string InputLineSyntax { get ; set; }
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
        Terminal.ConPTYTerm.WriteToUITerminal(input);
    }

    public void AddOutput(string output)
    {
        Terminal.ConPTYTerm.WriteToUITerminal(output);
    }


}
