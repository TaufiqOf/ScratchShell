using ScratchShell.ViewModels.Models;
using System.Windows.Controls;

namespace ScratchShell.UserControls;

/// <summary>
/// Interaction logic for FtpUserControl.xaml
/// </summary>
public partial class FtpUserControl : UserControl, IWorkspaceControl
{
    private ServerViewModel server;

    public FtpUserControl()
    {
        InitializeComponent();
    }

    public FtpUserControl(ServerViewModel server)
    {
        InitializeComponent();
        this.server = server;
        Terminal.InputLineSyntax = ">";
        Terminal.CommandEntered += TerminalSentMessage;
    }

    private void TerminalSentMessage(ITerminal obj, string command)
    {
        var output = $"{Terminal.Output}\nYou Said to the FTP=>{command}";
        Terminal.AddOutput(output);
    }

    public void Dispose()
    {
    }
}