using Wpf.Ui.Controls;

namespace ScratchShell.Views.Windows;

/// <summary>
/// Interaction logic for FullScreenWindow.xaml
/// </summary>
public partial class FullScreenWindow : FluentWindow
{
    public FullScreenWindow()
    {
        InitializeComponent();
    }

    public FullScreenWindow(object content, string name, object? menuControl = null)
    {
        InitializeComponent();
        this.Title = name;
        TitleBar.Title = name;
        RootContentDialog.Content = content;
        MenuControl.Content = menuControl;
    }
}