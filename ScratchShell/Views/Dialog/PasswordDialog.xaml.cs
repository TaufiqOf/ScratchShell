using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.Views.Dialog;

public partial class PasswordDialog : ContentDialog
{
    public PasswordDialog(IContentDialogService contentDialogService) : base(contentDialogService.GetDialogHost())
    {
        InitializeComponent();
    }

    protected override async void OnButtonClick(ContentDialogButton button)
    {
        base.OnButtonClick(button);
    }
}