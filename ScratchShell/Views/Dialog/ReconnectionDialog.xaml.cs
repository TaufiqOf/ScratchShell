using ScratchShell.ViewModels.Models;
using Wpf.Ui;
using Wpf.Ui.Controls;
using ScratchShell.Resources;

namespace ScratchShell.Views.Dialog;

public partial class ReconnectionDialog : ContentDialog
{
    public ServerViewModel Server { get; }
    public string ErrorMessage { get; }

    public ReconnectionDialog(IContentDialogService contentDialogService, ServerViewModel server, string errorMessage) 
        : base(contentDialogService.GetDialogHost())
    {
        InitializeComponent();
        Server = server;
        ErrorMessage = errorMessage;
        
        UpdateUI();
    }

    private void UpdateUI()
    {
        ServerInfoText.Text = string.Format(Langauge.Dialog_Reconnect_ConnectionTimedOutTemplate, Server.Name, Server.Host, Server.Port);
        ErrorDetailsText.Text = ErrorMessage;
    }
}