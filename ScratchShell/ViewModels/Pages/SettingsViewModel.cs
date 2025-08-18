using ScratchShell.Constants;
using ScratchShell.Enums;
using ScratchShell.Services;
using ScratchShell.Properties;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace ScratchShell.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private string _appVersion = String.Empty;

        [ObservableProperty]
        private string _credit = String.Empty;

        [ObservableProperty]
        private string _currentUsername = String.Empty;

        [ObservableProperty]
        private IEnumerable<ShellType> _shellTypes;

        private ShellType _shellType;

        public ShellType ShellType
        {
            get => _shellType;
            set
            {
                if (SetProperty(ref _shellType, value))
                {
                    Settings.Default.DefaultShellType = value.ToString();
                    Settings.Default.Save();
                }
            }
        }

        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;
        
        public SettingsViewModel()
        {
            ShellTypes = Enum.GetValues(typeof(ShellType)).Cast<ShellType>();
        }
        
        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();
            else
                RefreshUserInfo(); // Refresh user info when navigating back to settings

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void InitializeViewModel()
        {
            CurrentTheme = ApplicationThemeManager.GetAppTheme();
            Credit = ApplicationConstant.Credit;
            AppVersion = $"{ApplicationConstant.Name} - {GetAssemblyVersion()}";
            ShellType = CommonService.GetEnumValue<ShellType>(Settings.Default.DefaultShellType);
            
            RefreshUserInfo();
            
            _isInitialized = true;
        }

        private void RefreshUserInfo()
        {
            // Get current username from stored settings
            var username = UserSettingsService.GetStoredUsername();
            CurrentUsername = !string.IsNullOrEmpty(username) ? $"Logged in as: {username}" : "Not logged in";
        }
        
        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        [RelayCommand]
        private void OnChangeTheme(string parameter)
        {
            switch (parameter)
            {
                case "theme_light":
                    if (CurrentTheme == ApplicationTheme.Light)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    CurrentTheme = ApplicationTheme.Light;

                    break;

                default:
                    if (CurrentTheme == ApplicationTheme.Dark)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    CurrentTheme = ApplicationTheme.Dark;

                    break;
            }
            Settings.Default.CurrentTheme = CurrentTheme.ToString();
            Settings.Default.Save();
        }

        [RelayCommand]
        private void OnLogout()
        {
            try
            {
                // Clear all stored authentication data
                AuthenticationService.Logout();
                
                // Update UI to reflect logout state
                CurrentUsername = "Not logged in";

                // Restart the application to show login dialog
                // This is the cleanest way to handle logout in a WPF app
                Application.Current.Shutdown();
                
                // Alternative: If you have access to MainWindowViewModel, you could call its Logout method
                // But since this is in SettingsViewModel, restarting is simpler
            }
            catch (Exception ex)
            {
                // Log error but don't crash the app
                System.Diagnostics.Debug.WriteLine($"Error during logout: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if user is currently authenticated
        /// </summary>
        public bool IsUserAuthenticated => AuthenticationService.IsTokenValid();

        /// <summary>
        /// For testing purposes - resets first time login flag
        /// </summary>
        [RelayCommand]
        private void OnResetFirstTimeLogin()
        {
            UserSettingsService.ResetFirstTimeLogin();
            System.Diagnostics.Debug.WriteLine("First time login flag has been reset");
        }
    }
}
