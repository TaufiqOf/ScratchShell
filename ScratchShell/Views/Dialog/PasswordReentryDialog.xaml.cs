using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.Views.Dialog;

public partial class PasswordReentryDialog : ContentDialog
{
    public string Password => PasswordBox.Password;
    public bool IsPasswordEntrySuccessful { get; private set; } = false;

    public PasswordReentryDialog(IContentDialogService contentDialogService, string username) : base(contentDialogService.GetDialogHost())
    {
        InitializeComponent();

        // Set the username in the dialog
        UsernameTextBlock.Text = username;

        // Focus on password field
        PasswordBox.Focus();
    }

    protected override async void OnButtonClick(ContentDialogButton button)
    {
        if (button == ContentDialogButton.Primary)
        {
            await HandlePasswordEntryAsync();
        }
        else
        {
            base.OnButtonClick(button);
        }
    }

    private async Task HandlePasswordEntryAsync()
    {
        // Validate password
        if (string.IsNullOrWhiteSpace(Password))
        {
            ShowStatusMessage("Password is required", Colors.Red);
            return;
        }

        // Show loading state
        SetLoadingState(true);

        try
        {
            var username = UsernameTextBlock.Text;

            // Try to initialize encryption keys with the provided password
            if (ScratchShell.Services.AuthenticationService.InitializeEncryptionKeys(Password))
            {
                ShowStatusMessage("Password verified successfully!", Colors.Green);
                IsPasswordEntrySuccessful = true;

                // Small delay to show success message
                await Task.Delay(1000);

                base.OnButtonClick(ContentDialogButton.Primary);
            }
            else
            {
                ShowStatusMessage("Invalid password. Please try again.", Colors.Red);
                IsPasswordEntrySuccessful = false;
            }
        }
        catch (Exception ex)
        {
            ShowStatusMessage($"Error: {ex.Message}", Colors.Red);
            IsPasswordEntrySuccessful = false;
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private void SetLoadingState(bool isLoading)
    {
        LoadingProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox.IsEnabled = !isLoading;
        IsPrimaryButtonEnabled = !isLoading;
        IsSecondaryButtonEnabled = !isLoading;
    }

    private void ShowStatusMessage(string message, Color color)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = new SolidColorBrush(color);
        StatusTextBlock.Visibility = Visibility.Visible;
    }

    private void HideStatusMessage()
    {
        StatusTextBlock.Visibility = Visibility.Collapsed;
    }
}