using ScratchShell.ViewModels.Models;
using Wpf.Ui;
using Wpf.Ui.Controls;

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
        ServerInfoText.Text = $"Connection to '{Server.Name}' ({Server.Host}:{Server.Port}) has timed out.";
        ErrorDetailsText.Text = ErrorMessage;
    }
}