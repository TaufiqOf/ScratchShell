using ScratchShell.Services;
using System.Net.Http;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.Views.Dialog;

public partial class MessageDialog : ContentDialog
{
    public MessageDialog(IContentDialogService contentDialogService, string title ,string message) : base(contentDialogService.GetDialogHost())
    {
        InitializeComponent();
        this.Title = title;
        txtMessage.Text = message;
    }
}