using ScratchShell.UserControls.TerminalControl;
using ScratchShell.ViewModels.Models;
using System.Windows.Controls;

namespace ScratchShell.UserControls
{
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

        public void Dispose()
        {
        }

        private void TerminalSentMessage(TerminalUserControl control, string arg2)
        {
            var output = $"{Terminal.Output}\nYou Said to the FTP=>{arg2}";
            Terminal.AddOutput(output);
        }
    }
}