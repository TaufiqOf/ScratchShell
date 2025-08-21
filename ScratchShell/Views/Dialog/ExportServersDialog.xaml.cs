using Microsoft.Win32;
using ScratchShell.Services;
using ScratchShell.ViewModels.Models;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.Views.Dialog;

public partial class ExportServersDialog : ContentDialog
{
    public string Password => PasswordInput.Password;

    public IContentDialogService ContentDialogService { get; }

    public ExportServersDialog(IContentDialogService contentDialogService, IEnumerable<ServerViewModel> servers) : base(contentDialogService.GetDialogHost())
    {
        InitializeComponent();
        ServersListBox.ItemsSource = servers;
        ContentDialogService = contentDialogService;
    }

    protected override async void OnButtonClick(ContentDialogButton button)
    {
        if (button == ContentDialogButton.Primary)
        {
            var isValid = true;
            if (ServersListBox.SelectedItems.Count == 0)
            {
                ServerListTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                isValid = false;
            }
            else
            {
                ServerListTextBlock.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
            }
            if (string.IsNullOrEmpty(ExportFilePathTextBox.Text))
            {
                ExportFilePathTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                isValid = false;
            }
            else
            {
                ExportFilePathTextBlock.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
            }
            if (string.IsNullOrEmpty(PasswordInput.Text))
            {
                PasswordTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                isValid = false;
            }
            else
            {
                PasswordTextBlock.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
            }
            if (!isValid)
            {
                return;
            }

            await ServerExportImportService.ExportServers(
                ServersListBox.SelectedItems.Cast<ServerViewModel>().Select(s => s.ToServer(true)).ToList(),
                ExportFilePathTextBox.Text,
                PasswordInput.Password);

            base.OnButtonClick(button);
        }
        else
        {
            base.OnButtonClick(button);
        }
    }

    private void ExportFilePathBrowseButtonClick(object sender, RoutedEventArgs e)
    {
        var saveDialog = new SaveFileDialog();
        saveDialog.Filter = "(*.ss)|*.ss|All Files (*.*)|*.*";
        if (saveDialog.ShowDialog() == true)
        {
            ExportFilePathTextBox.Text = saveDialog.FileName;
        }
    }

    private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        ServersListBox.SelectAll();
    }

    private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        // ServersListBox unselect all items
        foreach (var item in ServersListBox.Items)
        {
            ServersListBox.SelectedItems.Remove(item);
        }
    }
}