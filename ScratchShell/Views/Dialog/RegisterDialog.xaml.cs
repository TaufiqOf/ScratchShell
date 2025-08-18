using ScratchShell.Services;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.Views.Dialog
{
    public partial class RegisterDialog : ContentDialog
    {
        private readonly AuthenticationService _authenticationService;
        
        public string FirstName => FirstNameTextBox.Text;
        public string LastName => LastNameTextBox.Text;
        public string Email => EmailTextBox.Text;
        public string UserName => UserNameTextBox.Text;
        public string Password => PasswordBox.Password;
        public string ConfirmPassword => ConfirmPasswordBox.Password;
        public string Token { get; private set; } = string.Empty;
        public bool IsRegistrationSuccessful { get; private set; } = false;
        public bool IsFirstTimeLogin { get; private set; } = true; // Registration is always first time

        public RegisterDialog(IContentDialogService contentDialogService) : base(contentDialogService.GetDialogHost())
        {
            InitializeComponent();
            _authenticationService = new AuthenticationService(new HttpClient());
        }

        public RegisterDialog(IContentDialogService contentDialogService, AuthenticationService registerService) : base(contentDialogService.GetDialogHost())
        {
            InitializeComponent();
            _authenticationService = registerService;
        }

        protected override async void OnButtonClick(ContentDialogButton button)
        {
            if (button == ContentDialogButton.Primary)
            {
                await HandleRegisterAsync();
            }
            else
            {
                base.OnButtonClick(button);
            }
        }

        private async Task HandleRegisterAsync()
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
                // Call register API
                var result = await _authenticationService.RegisterAsync(
                    Email, 
                    Password, 
                    ConfirmPassword, 
                    FirstName, 
                    LastName, 
                    string.IsNullOrWhiteSpace(UserName) ? null : UserName);
                
                if (result.IsSuccess)
                {
                    AuthenticationService.Token = result.Token;
                    Token = result.Token;
                    IsRegistrationSuccessful = true;
                    IsFirstTimeLogin = result.IsFirstTimeLogin;
                    
                    ShowStatusMessage("Registration successful! Your credentials have been saved for future logins.", Colors.Green);
                    
                    // Small delay to show success message
                    await Task.Delay(1500);
                    
                    base.OnButtonClick(ContentDialogButton.Primary);
                }
                else
                {
                    ShowStatusMessage(result.Message, Colors.Red);
                    IsRegistrationSuccessful = false;
                }
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Registration failed: {ex.Message}", Colors.Red);
                IsRegistrationSuccessful = false;
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private bool ValidateInput()
        {
            var isValid = true;

            // Reset all field colors to default
            ResetFieldColors();

            // Validate first name
            if (string.IsNullOrWhiteSpace(FirstName))
            {
                FirstNameTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                ShowStatusMessage("First name is required", Colors.Red);
                isValid = false;
            }

            // Validate last name
            if (string.IsNullOrWhiteSpace(LastName))
            {
                LastNameTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                if (isValid) ShowStatusMessage("Last name is required", Colors.Red);
                isValid = false;
            }

            // Validate email
            if (string.IsNullOrWhiteSpace(Email))
            {
                EmailTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                if (isValid) ShowStatusMessage("Email is required", Colors.Red);
                isValid = false;
            }
            else if (!IsValidEmail(Email))
            {
                EmailTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                if (isValid) ShowStatusMessage("Please enter a valid email address", Colors.Red);
                isValid = false;
            }

            // Validate password
            if (string.IsNullOrWhiteSpace(Password))
            {
                PasswordTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                if (isValid) ShowStatusMessage("Password is required", Colors.Red);
                isValid = false;
            }
            else if (Password.Length < 6)
            {
                PasswordTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                if (isValid) ShowStatusMessage("Password must be at least 6 characters long", Colors.Red);
                isValid = false;
            }

            // Validate confirm password
            if (string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                ConfirmPasswordTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                if (isValid) ShowStatusMessage("Please confirm your password", Colors.Red);
                isValid = false;
            }
            else if (Password != ConfirmPassword)
            {
                PasswordTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                ConfirmPasswordTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                if (isValid) ShowStatusMessage("Passwords do not match", Colors.Red);
                isValid = false;
            }

            if (isValid)
            {
                HideStatusMessage();
            }

            return isValid;
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
                return emailRegex.IsMatch(email);
            }
            catch
            {
                return false;
            }
        }

        private void ResetFieldColors()
        {
            var defaultBrush = (Brush)FindResource("TextFillColorPrimaryBrush");
            FirstNameTextBlock.Foreground = defaultBrush;
            LastNameTextBlock.Foreground = defaultBrush;
            EmailTextBlock.Foreground = defaultBrush;
            UserNameTextBlock.Foreground = defaultBrush;
            PasswordTextBlock.Foreground = defaultBrush;
            ConfirmPasswordTextBlock.Foreground = defaultBrush;
        }

        private void SetLoadingState(bool isLoading)
        {
            LoadingProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            FirstNameTextBox.IsEnabled = !isLoading;
            LastNameTextBox.IsEnabled = !isLoading;
            EmailTextBox.IsEnabled = !isLoading;
            UserNameTextBox.IsEnabled = !isLoading;
            PasswordBox.IsEnabled = !isLoading;
            ConfirmPasswordBox.IsEnabled = !isLoading;
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