using ScratchShell.Constants;
using ScratchShell.Services;
using ScratchShell.View.Dialog;
using ScratchShell.ViewModels.Models;
using ScratchShell.Views.Dialog;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.ViewModels.Windows
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IContentDialogService _contentDialogService;

        public MainWindowViewModel(IContentDialogService contentDialogService)
        {
            this._contentDialogService = contentDialogService;
        }

        private async void ShowLogin()
        {
            if (_contentDialogService is not null)
            {
                var loginDialog = new LoginDialog(_contentDialogService);

                var contentDialogResult = await loginDialog.ShowAsync();
                if(contentDialogResult == ContentDialogResult.Secondary)
                {
                    await Register();
                }
                if (contentDialogResult == ContentDialogResult.None)
                {
                    Application.Current.Shutdown();
                }
            }
        }

        private async Task Register()
        {
            if (_contentDialogService is not null)
            {
                var registerDialog = new RegisterDialog(_contentDialogService);

                var contentDialogResult = await registerDialog.ShowAsync();
                if (contentDialogResult == ContentDialogResult.Primary)
                {
                    // Registration successful, continue with the app
                    if (registerDialog.IsRegistrationSuccessful)
                    {
                        // Registration automatically stores credentials for first-time login
                        // Continue to the main app
                        return;
                    }
                }
                else if (contentDialogResult == ContentDialogResult.Secondary)
                {
                    // Back to login button pressed
                    ShowLogin();
                }
                else if (contentDialogResult == ContentDialogResult.None)
                {
                    // Cancel or close - back to login
                    Application.Current.Shutdown();
                }
            }
        }

        internal void Loaded()
        {
            // Check if user has stored credentials for auto-login
            if (TryAutoLogin())
            {
                // User is already authenticated, continue to main app
                return;
            }

            // No stored credentials, show login dialog
            ShowLogin();
        }

        /// <summary>
        /// Attempts to automatically log in the user using stored credentials
        /// </summary>
        /// <returns>True if auto-login was successful, false otherwise</returns>
        private bool TryAutoLogin()
        {
            try
            {
                // Check if there are stored credentials
                var storedCredentials = UserSettingsService.GetStoredCredentials();
                if (!storedCredentials.HasValue || string.IsNullOrEmpty(storedCredentials.Value.token))
                {
                    return false;
                }

                // Set the token in the AuthenticationService
                AuthenticationService.Token = storedCredentials.Value.token;

                // Log successful auto-login (for debugging)
                System.Diagnostics.Debug.WriteLine($"Auto-login successful for user: {storedCredentials.Value.username}");

                return true;
            }
            catch (Exception ex)
            {
                // Log error and fall back to normal login
                System.Diagnostics.Debug.WriteLine($"Auto-login failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handles user logout by clearing stored credentials and showing login dialog
        /// </summary>
        public void Logout()
        {
            // Clear stored credentials
            AuthenticationService.Logout();

            // Show login dialog again
            ShowLogin();
        }

        /// <summary>
        /// Gets the currently logged in username from stored settings
        /// </summary>
        public string? GetCurrentUsername()
        {
            return UserSettingsService.GetStoredUsername();
        }

        /// <summary>
        /// Checks if user is currently authenticated
        /// </summary>
        public bool IsUserAuthenticated()
        {
            return AuthenticationService.IsTokenValid();
        }

        [ObservableProperty]
        private string _applicationTitle = ApplicationConstant.Name;

        [ObservableProperty]
        private ObservableCollection<object> _menuItems = new()
        {
            new NavigationViewItem()
            {
                Content = "Home",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
                TargetPageType = typeof(Views.Pages.DashboardPage),
            },
            new NavigationViewItem()
            {
                Content = "Session",
                Icon = new SymbolIcon { Symbol = SymbolRegular.DataHistogram24 },
                TargetPageType = typeof(Views.Pages.SessionPage)
            }
        };

        [ObservableProperty]
        private ObservableCollection<object> _footerMenuItems = new()
        {
            new NavigationViewItem()
            {
                Content = "Settings",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                TargetPageType = typeof(Views.Pages.SettingsPage)
            }
        };

        [ObservableProperty]
        private ObservableCollection<MenuItem> _trayMenuItems = new()
        {
            new MenuItem { Header = "Home", Tag = "tray_home" }
        };
    }
}
