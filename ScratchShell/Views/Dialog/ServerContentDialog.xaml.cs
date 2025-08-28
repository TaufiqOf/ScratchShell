using Microsoft.Win32;
using ScratchShell.ViewModels.Models;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using TextBox = Wpf.Ui.Controls.TextBox;
using ScratchShell.Resources;

namespace ScratchShell.View.Dialog;

/// <summary>
/// Interaction logic for ServerContentDialog.xaml
/// </summary>
public partial class ServerContentDialog : ContentDialog
{
    public ServerViewModel ViewModel { get; }

    public ServerContentDialog(ContentPresenter? contentPresenter, ServerViewModel viewModel)
        : base(contentPresenter)
    {
        InitializeComponent();
        this.ViewModel = viewModel;
        PasswordInput.Password = viewModel.Password;
        KeyFilePasswordInput.Password = viewModel.KeyFilePassword;
        DataContext = new ServerViewModel(viewModel, viewModel.ContentDialogService);
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ServerViewModel viewModel)
        {
            viewModel.Password = PasswordInput.Password;
        }
    }

    private void KeyFilePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ServerViewModel viewModel)
        {
            viewModel.KeyFilePassword = KeyFilePasswordInput.Password;
        }
    }

    protected override void OnButtonClick(ContentDialogButton button)
    {
        if (button == ContentDialogButton.Primary)
        {
            if (DataContext is ServerViewModel viewModel)
            {
                // Check if the form is valid
                if (!viewModel.IsValid)
                {
                    // Find and show validation errors
                    ShowValidationErrors();
                    return;
                }

                // Update the original view model with validated data
                ViewModel.Name = viewModel.Name;
                ViewModel.Host = viewModel.Host;
                ViewModel.Port = viewModel.Port;
                ViewModel.Username = viewModel.Username;
                ViewModel.UseKeyFile = viewModel.UseKeyFile;
                ViewModel.Password = viewModel.Password;
                ViewModel.KeyFilePassword = viewModel.KeyFilePassword;
                ViewModel.ProtocolType = viewModel.ProtocolType;
                ViewModel.PrivateKeyFilePath = viewModel.PrivateKeyFilePath;
                ViewModel.PublicKeyFilePath = viewModel.PublicKeyFilePath;
            }
        }
        base.OnButtonClick(button);
    }

    private void ShowValidationErrors()
    {
        // Force validation on all named textboxes by finding them
        var nameTextBox = FindChild<TextBox>(this, "NameTextBox");
        var hostTextBox = FindChild<TextBox>(this, "HostTextBox");
        var portTextBox = FindChild<TextBox>(this, "PortTextBox");
        var usernameTextBox = FindChild<TextBox>(this, "UsernameTextBox");
        var validationSummary = FindChild<Border>(this, "ValidationSummary");
        var validationSummaryText = FindChild<System.Windows.Controls.TextBlock>(this, "ValidationSummaryText");

        // Force validation updates
        nameTextBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        hostTextBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        portTextBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        usernameTextBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        // Show validation summary
        if (validationSummary != null && validationSummaryText != null && DataContext is ServerViewModel viewModel)
        {
            var errors = new List<string>();

            var nameError = viewModel[nameof(viewModel.Name)];
            if (!string.IsNullOrEmpty(nameError))
                errors.Add($"• {nameError}");

            var hostError = viewModel[nameof(viewModel.Host)];
            if (!string.IsNullOrEmpty(hostError))
                errors.Add($"• {hostError}");

            var portError = viewModel[nameof(viewModel.Port)];
            if (!string.IsNullOrEmpty(portError))
                errors.Add($"• {portError}");

            var usernameError = viewModel[nameof(viewModel.Username)];
            if (!string.IsNullOrEmpty(usernameError))
                errors.Add($"• {usernameError}");

            if (errors.Any())
            {
                validationSummaryText.Text = string.Join("\n", errors);
                validationSummary.Visibility = System.Windows.Visibility.Visible;
            }
        }
    }

    // Helper method to find child controls by name
    private static T? FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
    {
        if (parent == null) return null;

        T? foundChild = null;

        int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is T t && (child as FrameworkElement)?.Name == childName)
            {
                foundChild = t;
                break;
            }

            foundChild = FindChild<T>(child, childName);
            if (foundChild != null) break;
        }

        return foundChild;
    }

    private void PublicKeyFilePath_Click(object sender, RoutedEventArgs e)
    {
        var openDialog = new OpenFileDialog
        {
            Filter = Langauge.FileFilter_PublicKey,
            DefaultExt = ".pub"
        };
        if (openDialog.ShowDialog() == true)
        {
            if (DataContext is ServerViewModel viewModel)
            {
                viewModel.PublicKeyFilePath = openDialog.FileName;
            }
        }
    }

    private void PrivateKeyFilePath_Click(object sender, RoutedEventArgs e)
    {
        var openDialog = new OpenFileDialog
        {
            Filter = Langauge.FileFilter_PrivateKey,
            DefaultExt = ".pem"
        };
        if (openDialog.ShowDialog() == true)
        {
            if (DataContext is ServerViewModel viewModel)
            {
                viewModel.PrivateKeyFilePath = openDialog.FileName;
            }
        }
    }
}