using ScratchShell.Constants;
using ScratchShell.Enums;
using ScratchShell.Properties;
using ScratchShell.Services;
using System.Diagnostics;
using System.Net.Http;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ScratchShell.ViewModels.Pages;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;
    private CloudSyncService? _cloudSyncService;
    private readonly IContentDialogService _contentDialogService;

    [ObservableProperty]
    private string _appVersion = String.Empty;

    [ObservableProperty]
    private string _credit = String.Empty;

    [ObservableProperty]
    private string _currentUsername = String.Empty;

    [ObservableProperty]
    private IEnumerable<ShellType> _shellTypes;

    [ObservableProperty]
    private bool _enableCloudSync = true;

    [ObservableProperty]
    private bool _autoSyncOnStartup = true;

    [ObservableProperty]
    private bool _autoSyncOnChange = true;

    [ObservableProperty]
    private string _lastSyncStatus = "Never synced";

    [ObservableProperty]
    private bool _isSyncing = false;

    [ObservableProperty]
    private string _syncStatusMessage = string.Empty;

    [ObservableProperty]
    private IEnumerable<LanguageInfo> _availableLanguages;

    private ShellType _shellType;
    private LanguageInfo _selectedLanguage;

    public ShellType ShellType
    {
        get => _shellType;
        set
        {
            if (SetProperty(ref _shellType, value))
            {
                Settings.Default.DefaultShellType = value.ToString();
                Settings.Default.Save();

                // Trigger cloud sync if enabled
                _ = Task.Run(async () => await UserSettingsService.TriggerCloudSyncIfEnabled());
            }
        }
    }

    public LanguageInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
            {
                LocalizationManager.ChangeLanguage(value.Code);

                // Trigger cloud sync if enabled
                _ = Task.Run(async () => await UserSettingsService.TriggerCloudSyncIfEnabled());

                // Show message about restart requirement
                ShowLanguageChangeMessage();
            }
        }
    }

    [ObservableProperty]
    private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

    public SettingsViewModel(IContentDialogService contentDialogService)
    {
        _contentDialogService = contentDialogService;
        ShellTypes = Enum.GetValues(typeof(ShellType)).Cast<ShellType>();
        AvailableLanguages = LocalizationManager.SupportedLanguages.Values;
        _selectedLanguage = LocalizationManager.CurrentLanguageInfo;

        // Subscribe to language changes to ensure the UI stays updated
        LocalizationManager.LanguageChanged += OnLanguageChanged;

        InitializeCloudSync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Update the selected language if it was changed externally
        var currentLanguage = LocalizationManager.CurrentLanguageInfo;
        if (_selectedLanguage.Code != currentLanguage.Code)
        {
            _selectedLanguage = currentLanguage;
            OnPropertyChanged(nameof(SelectedLanguage));
        }
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
        SelectedLanguage = LocalizationManager.CurrentLanguageInfo;

        RefreshUserInfo();
        RefreshCloudSyncInfo();

        _isInitialized = true;
    }

    private void InitializeCloudSync()
    {
        try
        {
            _cloudSyncService = new CloudSyncService(new HttpClient());
            UserSettingsService.InitializeCloudSync(_cloudSyncService);

            // Subscribe to sync events
            _cloudSyncService.SyncStatusChanged += OnSyncStatusChanged;
            _cloudSyncService.ConflictDetected += OnConflictDetected;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing cloud sync: {ex.Message}");
        }
    }

    private void RefreshUserInfo()
    {
        // Get current username from stored settings
        var username = UserSettingsService.GetStoredUsername();
        CurrentUsername = !string.IsNullOrEmpty(username) ? $"Logged in as: {username}" : "Not logged in";
    }

    private void RefreshCloudSyncInfo()
    {
        var (enabled, autoStartup, autoChange) = UserSettingsService.GetCloudSyncSettings();
        EnableCloudSync = enabled;
        AutoSyncOnStartup = autoStartup;
        AutoSyncOnChange = autoChange;

        var lastSync = UserSettingsService.GetLastSyncTimestamp();
        if (lastSync.HasValue)
        {
            var timeAgo = DateTime.Now - lastSync.Value;
            if (timeAgo.TotalMinutes < 1)
                LastSyncStatus = "Just now";
            else if (timeAgo.TotalHours < 1)
                LastSyncStatus = $"{(int)timeAgo.TotalMinutes} minutes ago";
            else if (timeAgo.TotalDays < 1)
                LastSyncStatus = $"{(int)timeAgo.TotalHours} hours ago";
            else
                LastSyncStatus = lastSync.Value.ToString("g");
        }
        else
        {
            LastSyncStatus = "Never synced";
        }
    }

    private void ShowLanguageChangeMessage()
    {
        try
        {
            var message = LocalizationManager.GetString("Language_ChangeMessage");
            var title = LocalizationManager.GetString("Settings_Language");
            var buttonText = LocalizationManager.GetString("Settings_Restart");
            var buttonSecondText = LocalizationManager.GetString("General_Close");

            // Show a simple message dialog
            _ = Task.Run(async () =>
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var messageDialog = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = title,
                        Content = message,
                        PrimaryButtonText = buttonText,
                        CloseButtonText = buttonSecondText
                    };

                    var result = await messageDialog.ShowDialogAsync();
                    if(result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                    {
                        RestartApp();
                    }
                });
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error showing language change message: {ex.Message}");
        }
    }
    private static void RestartApp()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule!.FileName!; 
            var args = Environment.GetCommandLineArgs().Skip(1); 
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a.Replace("\"", "\"") + "\"" : a)) 
            }; 
            Process.Start(psi); 
        } catch {
            /* log if desired */ 
        } finally { 
            Application.Current.Shutdown(); 
        } 
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

        // Trigger cloud sync if enabled
        _ = Task.Run(async () => await UserSettingsService.TriggerCloudSyncIfEnabled());
    }

    [RelayCommand]
    private async Task OnLogout()
    {
        try
        {
            // Before logout, try to sync current data to cloud to preserve it
            if (_cloudSyncService != null && AuthenticationService.IsTokenValid())
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        var result = await _cloudSyncService.SyncToCloudAsync();
                        if (result.IsSuccess)
                        {
                            System.Diagnostics.Debug.WriteLine("Data synced to cloud before logout");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to sync data before logout: {result.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error syncing before logout: {ex.Message}");
                    }
                    finally
                    {
                        // Proceed with logout after sync attempt
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            PerformLogout();
                        });
                    }
                });
            }
            else
            {
                // No cloud sync available, proceed with logout
                PerformLogout();
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash the app
            System.Diagnostics.Debug.WriteLine($"Error during logout: {ex.Message}");
            PerformLogout(); // Fallback to direct logout
        }
    }

    private void PerformLogout()
    {
        try
        {
            // Clear all stored authentication data and local settings
            AuthenticationService.Logout();

            // Update UI to reflect logout state
            CurrentUsername = "Not logged in";

            // Restart the application to show login dialog
            // This is the cleanest way to handle logout in a WPF app
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error performing logout: {ex.Message}");
            // Force shutdown even if logout fails
            Application.Current.Shutdown();
        }
    }

    [RelayCommand]
    private async Task OnSync()
    {
        await OnSyncFromCloud();
        await OnSyncToCloud();
    }

    private async Task OnSyncToCloud()
    {
        if (_cloudSyncService == null || !AuthenticationService.IsTokenValid())
            return;

        try
        {
            IsSyncing = true;
            SyncStatusMessage = "Syncing to cloud...";

            var result = await _cloudSyncService.SyncToCloudAsync();

            if (result.IsSuccess)
            {
                SyncStatusMessage = "Sync to cloud successful";
                RefreshCloudSyncInfo();
            }
            else if (result.RequiresPasswordReentry)
            {
                // Show password re-entry dialog
                await HandlePasswordReentryForSync(async () => await OnSyncToCloud());
            }
            else
            {
                SyncStatusMessage = $"Sync failed: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            SyncStatusMessage = $"Sync error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
            // Clear status message after 3 seconds
            _ = Task.Delay(3000).ContinueWith(_ => SyncStatusMessage = string.Empty);
        }
    }

    private async Task OnSyncFromCloud()
    {
        if (_cloudSyncService == null || !AuthenticationService.IsTokenValid())
            return;

        try
        {
            IsSyncing = true;
            SyncStatusMessage = "Syncing from cloud...";

            var result = await _cloudSyncService.SyncFromCloudAsync();

            if (result.IsSuccess)
            {
                SyncStatusMessage = "Sync from cloud successful";
                RefreshCloudSyncInfo();

                // Refresh UI to reflect downloaded settings
                InitializeViewModel();
            }
            else if (result.RequiresPasswordReentry)
            {
                // Show password re-entry dialog
                await HandlePasswordReentryForSync(async () => await OnSyncFromCloud());
            }
            else
            {
                if (result.HasConflict)
                {
                    SyncStatusMessage = "Conflict detected. Please resolve manually.";
                    // TODO: Show conflict resolution dialog
                }
                else
                {
                    SyncStatusMessage = $"Sync failed: {result.Message}";
                }
            }
        }
        catch (Exception ex)
        {
            SyncStatusMessage = $"Sync error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
            // Clear status message after 3 seconds
            _ = Task.Delay(3000).ContinueWith(_ => SyncStatusMessage = string.Empty);
        }
    }

    private async Task HandlePasswordReentryForSync(Func<Task> retryAction)
    {
        try
        {
            var username = UserSettingsService.GetStoredUsername();
            if (string.IsNullOrEmpty(username))
            {
                SyncStatusMessage = "Unable to prompt for password - username not found";
                return;
            }

            // Create and show password re-entry dialog
            var passwordDialog = new Views.Dialog.PasswordReentryDialog(_contentDialogService, username);
            var result = await passwordDialog.ShowAsync();

            if (result == ContentDialogResult.Primary && passwordDialog.IsPasswordEntrySuccessful)
            {
                // Password was successfully entered and encryption keys initialized
                SyncStatusMessage = "Password verified. Retrying sync...";

                // Retry the original operation
                await retryAction();
            }
            else
            {
                SyncStatusMessage = "Cloud sync cancelled - password required for encryption";
            }
        }
        catch (Exception ex)
        {
            SyncStatusMessage = $"Error during password re-entry: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OnDeleteCloudSettings()
    {
        if (_cloudSyncService == null || !AuthenticationService.IsTokenValid())
            return;

        try
        {
            IsSyncing = true;
            SyncStatusMessage = "Deleting cloud settings...";

            var result = await _cloudSyncService.DeleteCloudSettingsAsync();

            if (result.IsSuccess)
            {
                SyncStatusMessage = "Cloud settings deleted successfully";
                RefreshCloudSyncInfo(); // Refresh to show "Never synced"
            }
            else
            {
                SyncStatusMessage = $"Delete failed: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            SyncStatusMessage = $"Delete error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
            // Clear status message after 3 seconds
            _ = Task.Delay(3000).ContinueWith(_ => SyncStatusMessage = string.Empty);
        }
    }

    [RelayCommand]
    private void OnUpdateCloudSyncSettings()
    {
        UserSettingsService.UpdateCloudSyncSettings(EnableCloudSync, AutoSyncOnStartup, AutoSyncOnChange);
        SyncStatusMessage = "Cloud sync settings updated";

        // Clear status message after 2 seconds
        _ = Task.Delay(2000).ContinueWith(_ => SyncStatusMessage = string.Empty);
    }

    private void OnSyncStatusChanged(object? sender, SyncStatusEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SyncStatusMessage = e.Message;
            IsSyncing = e.Status == SyncStatus.Uploading || e.Status == SyncStatus.Downloading;

            if (e.Status == SyncStatus.UploadCompleted || e.Status == SyncStatus.DownloadCompleted)
            {
                RefreshCloudSyncInfo();
            }
        });
    }

    private void OnConflictDetected(object? sender, ConflictDetectedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SyncStatusMessage = "Sync conflict detected. Manual resolution required.";

            // For now, we'll auto-resolve by using server settings since this might be a false positive
            // In the future, show a proper conflict resolution dialog
            if (_cloudSyncService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Auto-resolve by using server settings for now
                        var result = await _cloudSyncService.ResolveConflictAsync(ConflictResolution.UseServer);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (result.IsSuccess)
                            {
                                SyncStatusMessage = "Conflict resolved automatically using server settings";
                                RefreshCloudSyncInfo();
                                InitializeViewModel(); // Refresh UI
                            }
                            else
                            {
                                SyncStatusMessage = $"Failed to resolve conflict: {result.Message}";
                            }

                            // Clear status message after 5 seconds
                            _ = Task.Delay(5000).ContinueWith(_ => SyncStatusMessage = string.Empty);
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            SyncStatusMessage = $"Error resolving conflict: {ex.Message}";
                            _ = Task.Delay(5000).ContinueWith(_ => SyncStatusMessage = string.Empty);
                        });
                    }
                });
            }
        });
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