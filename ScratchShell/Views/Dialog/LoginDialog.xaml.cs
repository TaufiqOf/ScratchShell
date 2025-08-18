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
        public bool IsFirstTimeLogin { get; private set; } = false;

        public LoginDialog(IContentDialogService contentDialogService) : base(contentDialogService.GetDialogHost())
        {
            InitializeComponent();
            _loginService = new AuthenticationService(new HttpClient());
#if DEBUG
            UsernameTextBox.Text = "dfsdf@asd.com";
            PasswordBox.Password = "123456Ta";
#endif
            // Pre-fill username if stored
            var storedUsername = UserSettingsService.GetStoredUsername();
            if (!string.IsNullOrEmpty(storedUsername))
            {
                UsernameTextBox.Text = storedUsername;
                // Focus on password field if username is pre-filled
                PasswordBox.Focus();
            }
        }

        public LoginDialog(IContentDialogService contentDialogService, AuthenticationService loginService) : base(contentDialogService.GetDialogHost())
        {
            InitializeComponent();
            _loginService = loginService;
            
            // Pre-fill username if stored
            var storedUsername = UserSettingsService.GetStoredUsername();
            if (!string.IsNullOrEmpty(storedUsername))
            {
                UsernameTextBox.Text = storedUsername;
                // Focus on password field if username is pre-filled
                PasswordBox.Focus();
            }
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
                    AuthenticationService.Token = result.Token;
                    //SecureKeyStore.InitializeForUser(Username);
                    Token = result.Token;
                    IsLoginSuccessful = true;
                    IsFirstTimeLogin = result.IsFirstTimeLogin;
                    
                    // Show appropriate success message
                    if (result.IsFirstTimeLogin)
                    {
                        ShowStatusMessage("Welcome! Your credentials have been saved for future logins.", Colors.Green);
                    }
                    else
                    {
                        ShowStatusMessage("Login successful!", Colors.Green);
                    }
                    
                    // Small delay to show success message
                    await Task.Delay(1000);
                    
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

        /// <summary>
        /// Method to check if user has stored credentials for auto-login
        /// </summary>
        public static bool HasStoredCredentials()
        {
            return AuthenticationService.HasStoredCredentials();
        }

        /// <summary>
        /// Method to attempt auto-login with stored credentials
        /// </summary>
        public static bool TryAutoLogin()
        {
            var storedCredentials = UserSettingsService.GetStoredCredentials();
            if (storedCredentials.HasValue && !string.IsNullOrEmpty(storedCredentials.Value.token))
            {
                AuthenticationService.Token = storedCredentials.Value.token;
                return true;
            }
            return false;
        }
    }
}