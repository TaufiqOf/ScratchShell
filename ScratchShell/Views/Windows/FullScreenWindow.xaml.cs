using System.ComponentModel;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.Views.Windows;

/// <summary>
/// Interaction logic for FullScreenWindow.xaml
/// </summary>
public partial class FullScreenWindow : FluentWindow
{
    private IContentDialogService _contentDialogService;

    public FullScreenWindow(IContentDialogService contentDialogService)
    {
        InitializeComponent();
        _contentDialogService = contentDialogService;
        contentDialogService.SetDialogHost(RootContentDialog);
    }

    public FullScreenWindow(IContentDialogService contentDialogService, object content, string name, object? menuControl = null)
    {
        InitializeComponent();
        this.Title = name;
        TitleBar.Title = name;
        RootContentDialog.Content = content;
        MenuControl.Content = menuControl;
        _contentDialogService = contentDialogService;
        contentDialogService.SetDialogHost(RootContentDialog);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var rootContentDialog = ((MainWindow)Application.Current.MainWindow).RootContentDialog;
        _contentDialogService.SetDialogHost(rootContentDialog);
        base.OnClosing(e);
    }
}