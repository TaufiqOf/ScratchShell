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
    }

    private void TerminalSentMessage(ITerminal obj, string command)
    {
    }

    public void Dispose()
    {
    }
}