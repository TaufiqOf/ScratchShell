using Microsoft.Win32;
using ScratchShell.Services;
using ScratchShell.ViewModels.Models;
using System.Net.Http;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.Views.Dialog
{
    public partial class LoginDialog : ContentDialog
    {
        private readonly AuthenticationService _loginService;
        
        public string Username => UsernameTextBox.Text;
        public string Password => PasswordBox.Password;
        public string Token { get; private set; } = string.Empty;
        public bool IsLoginSuccessful { get; private set; } = false;

        public LoginDialog(IContentDialogService contentDialogService) : base(contentDialogService.GetDialogHost())
        {
            InitializeComponent();
            _loginService = new AuthenticationService(new HttpClient());
        }

        public LoginDialog(IContentDialogService contentDialogService, AuthenticationService loginService) : base(contentDialogService.GetDialogHost())
        {
            InitializeComponent();
            _loginService = loginService;
        }

        protected override async void OnButtonClick(ContentDialogButton button)
        {
            if (button == ContentDialogButton.Primary)
            {
                await HandleLoginAsync();
            }
            else
            {
                base.OnButtonClick(button);
            }
        }

        private async Task HandleLoginAsync()
        {
            // Validate input
            if (!ValidateInput())
            {
                return;
            }

            // Show loading state
            SetLoadingState(true);
            
            try
            {
                // Call login API
                var result = await _loginService.LoginAsync(Username, Password);
                
                if (result.IsSuccess)
                {
                    Token = result.Token;
                    IsLoginSuccessful = true;
                    ShowStatusMessage("Login successful!", Colors.Green);
                    
                    // Small delay to show success message
                    await Task.Delay(500);
                    
                    base.OnButtonClick(ContentDialogButton.Primary);
                }
                else
                {
                    ShowStatusMessage(result.Message, Colors.Red);
                    IsLoginSuccessful = false;
                }
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Login failed: {ex.Message}", Colors.Red);
                IsLoginSuccessful = false;
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private bool ValidateInput()
        {
            var isValid = true;

            // Validate username
            if (string.IsNullOrWhiteSpace(Username))
            {
                UsernameTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                ShowStatusMessage("Username is required", Colors.Red);
                isValid = false;
            }
            else
            {
                UsernameTextBlock.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
            }

            // Validate password
            if (string.IsNullOrWhiteSpace(Password))
            {
                PasswordTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                if (isValid) // Only show password error if username is valid
                {
                    ShowStatusMessage("Password is required", Colors.Red);
                }
                isValid = false;
            }
            else
            {
                PasswordTextBlock.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
            }

            if (isValid)
            {
                HideStatusMessage();
            }

            return isValid;
        }

        private void SetLoadingState(bool isLoading)
        {
            LoadingProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            UsernameTextBox.IsEnabled = !isLoading;
            PasswordBox.IsEnabled = !isLoading;
            IsPrimaryButtonEnabled = !isLoading;
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
}