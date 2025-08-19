using ScratchShell.Properties;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ScratchShell.Services
{
    public class UserSettingsService
    {
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("ScratchShellEntropy2024");
        private static CloudSyncService? _cloudSyncService;

        /// <summary>
        /// Initializes cloud sync service
        /// </summary>
        public static void InitializeCloudSync(CloudSyncService cloudSyncService)
        {
            _cloudSyncService = cloudSyncService;

            // Subscribe to settings changes for auto-sync
            if (Settings.Default.AutoSyncOnChange)
            {
                // We'll implement this when settings change events are available
            }
        }

        /// <summary>
        /// Checks if this is the user's first time logging in
        /// </summary>
        public static bool IsFirstTimeLogin()
        {
            return Settings.Default.IsFirstTimeLogin;
        }

        /// <summary>
        /// Stores the authentication token and username after first successful login
        /// </summary>
        /// <param name="token">JWT authentication token</param>
        /// <param name="username">Username/email of the user</param>
        /// <param name="rememberMe">Whether to remember the user for auto-login</param>
        public static async void StoreAuthenticationCredentials(string token, string username, bool rememberMe = true)
        {
            try
            {
                // Encrypt the token before storing for security
                var encryptedToken = ProtectData(token);

                Settings.Default.AuthToken = encryptedToken;
                Settings.Default.Username = username;
                Settings.Default.RememberMe = rememberMe;
                Settings.Default.IsFirstTimeLogin = false;
                Settings.Default.Save();

                // Update the AuthenticationService static token
                AuthenticationService.Token = token;

                // Trigger cloud sync if enabled and user is logging in for first time
                if (Settings.Default.EnableCloudSync && _cloudSyncService != null && AuthenticationService.IsTokenValid())
                {
                    await Task.Run(async () =>
                    {
                        try
                        {
                            // Try to download settings from cloud on first login
                            await _cloudSyncService.SyncFromCloudAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error during initial cloud sync: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // Log error but don't throw to avoid breaking login flow
                System.Diagnostics.Debug.WriteLine($"Error storing authentication credentials: {ex.Message}");
            }
        }

        /// <summary>
        /// Retrieves stored authentication credentials
        /// </summary>
        /// <returns>Tuple of (token, username, rememberMe) or null if not found/invalid</returns>
        public static (string? token, string? username, bool rememberMe)? GetStoredCredentials()
        {
            try
            {
                if (string.IsNullOrEmpty(Settings.Default.AuthToken) ||
                    string.IsNullOrEmpty(Settings.Default.Username) ||
                    !Settings.Default.RememberMe)
                {
                    return null;
                }

                // Decrypt the stored token
                var decryptedToken = UnprotectData(Settings.Default.AuthToken);

                if (string.IsNullOrEmpty(decryptedToken))
                {
                    return null;
                }

                return (decryptedToken, Settings.Default.Username, Settings.Default.RememberMe);
            }
            catch (Exception ex)
            {
                // Log error and return null if decryption fails
                System.Diagnostics.Debug.WriteLine($"Error retrieving stored credentials: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clears stored authentication credentials (logout)
        /// </summary>
        public static async void ClearStoredCredentials()
        {
            try
            {
                // Clear cloud settings if enabled
                if (Settings.Default.EnableCloudSync && _cloudSyncService != null && AuthenticationService.IsTokenValid())
                {
                    // Don't delete cloud settings, just stop syncing
                    // User might login again later
                }

                // Clear all local settings
                CloudSyncService.ClearAllLocalSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing stored credentials: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the stored username without decrypting token
        /// </summary>
        public static string? GetStoredUsername()
        {
            return string.IsNullOrEmpty(Settings.Default.Username) ? null : Settings.Default.Username;
        }

        /// <summary>
        /// Checks if user has enabled remember me
        /// </summary>
        public static bool IsRememberMeEnabled()
        {
            return Settings.Default.RememberMe;
        }

        /// <summary>
        /// Updates the remember me preference
        /// </summary>
        public static async void SetRememberMe(bool rememberMe)
        {
            Settings.Default.RememberMe = rememberMe;
            Settings.Default.Save();

            // If remember me is disabled, clear the stored token
            if (!rememberMe)
            {
                Settings.Default.AuthToken = string.Empty;
                Settings.Default.Save();
            }

            // Trigger cloud sync if enabled
            await TriggerCloudSyncIfEnabled();
        }

        /// <summary>
        /// Resets first time login flag (for testing purposes)
        /// </summary>
        public static void ResetFirstTimeLogin()
        {
            Settings.Default.IsFirstTimeLogin = true;
            Settings.Default.Save();
        }

        /// <summary>
        /// Updates cloud sync settings
        /// </summary>
        public static async void UpdateCloudSyncSettings(bool enableCloudSync, bool autoSyncOnStartup, bool autoSyncOnChange)
        {
            Settings.Default.EnableCloudSync = enableCloudSync;
            Settings.Default.AutoSyncOnStartup = autoSyncOnStartup;
            Settings.Default.AutoSyncOnChange = autoSyncOnChange;
            Settings.Default.Save();

            // If cloud sync was just enabled, trigger initial sync
            if (enableCloudSync && _cloudSyncService != null && AuthenticationService.IsTokenValid())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cloudSyncService.SyncToCloudAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error during cloud sync setup: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Triggers cloud sync if enabled and conditions are met
        /// </summary>
        public static async Task TriggerCloudSyncIfEnabled()
        {
            if (Settings.Default.EnableCloudSync &&
                Settings.Default.AutoSyncOnChange &&
                _cloudSyncService != null &&
                AuthenticationService.IsTokenValid())
            {
                try
                {
                    // Check if we have encryption keys available
                    if (!AuthenticationService.HasCloudEncryptionKeys)
                    {
                        System.Diagnostics.Debug.WriteLine("Cloud sync requested but encryption keys not available (auto-login scenario)");
                        // In this case, skip the sync - user would need to manually sync or re-login
                        return;
                    }

                    await _cloudSyncService.SyncToCloudAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during auto cloud sync: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Performs startup sync if enabled
        /// </summary>
        public static async Task PerformStartupSyncIfEnabled()
        {
            if (Settings.Default.EnableCloudSync &&
                Settings.Default.AutoSyncOnStartup &&
                _cloudSyncService != null &&
                AuthenticationService.IsTokenValid())
            {
                try
                {
                    // Check if we need to restore servers from cloud due to encryption key changes
                    if (ServerManager.NeedsCloudRestore)
                    {
                        System.Diagnostics.Debug.WriteLine("Attempting to restore servers from cloud due to encryption key changes");

                        // Try to download from cloud first to restore data
                        var downloadResult = await _cloudSyncService.SyncFromCloudAsync();
                        if (downloadResult.IsSuccess)
                        {
                            System.Diagnostics.Debug.WriteLine("Successfully restored servers from cloud");
                            ServerManager.ClearOldEncryptedData();
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to restore servers from cloud: {downloadResult.Message}");
                        }
                    }
                    else
                    {
                        // Normal startup sync
                        await _cloudSyncService.SyncFromCloudAsync();
                        await _cloudSyncService.SyncToCloudAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during startup cloud sync: {ex.Message}");
                }
            }
            else if (ServerManager.NeedsCloudRestore)
            {
                System.Diagnostics.Debug.WriteLine("Cloud sync is disabled but servers need restoration - data may be lost");
            }
        }

        /// <summary>
        /// Gets cloud sync status
        /// </summary>
        public static (bool enabled, bool autoStartup, bool autoChange) GetCloudSyncSettings()
        {
            return (
                Settings.Default.EnableCloudSync,
                Settings.Default.AutoSyncOnStartup,
                Settings.Default.AutoSyncOnChange
            );
        }

        /// <summary>
        /// Gets last sync timestamp
        /// </summary>
        public static DateTime? GetLastSyncTimestamp()
        {
            // First try to read from Settings
            if (!string.IsNullOrEmpty(Settings.Default.LastSyncTimestamp) &&
                DateTime.TryParse(Settings.Default.LastSyncTimestamp, out var timestamp))
            {
                return timestamp;
            }

            // Fallback to file-based timestamp (for backward compatibility)
            try
            {
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScratchShell");
                var syncFile = Path.Combine(appDataPath, "lastsync.txt");
                if (File.Exists(syncFile))
                {
                    var timestampStr = File.ReadAllText(syncFile);
                    if (DateTime.TryParse(timestampStr, out var fileTimestamp))
                    {
                        // Migrate file timestamp to Settings
                        UpdateLastSyncTimestamp(fileTimestamp);
                        return fileTimestamp;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading sync timestamp from file: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Updates last sync timestamp
        /// </summary>
        public static void UpdateLastSyncTimestamp(DateTime timestamp)
        {
            Settings.Default.LastSyncTimestamp = timestamp.ToString("O");
            Settings.Default.Save();
        }

        /// <summary>
        /// Encrypts sensitive data using Windows Data Protection API
        /// </summary>
        private static string ProtectData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return string.Empty;

            try
            {
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                byte[] protectedData = ProtectedData.Protect(dataBytes, _entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(protectedData);
            }
            catch
            {
                // If encryption fails, return empty string for security
                return string.Empty;
            }
        }

        /// <summary>
        /// Decrypts sensitive data using Windows Data Protection API
        /// </summary>
        private static string UnprotectData(string protectedData)
        {
            if (string.IsNullOrEmpty(protectedData))
                return string.Empty;

            try
            {
                byte[] protectedBytes = Convert.FromBase64String(protectedData);
                byte[] dataBytes = ProtectedData.Unprotect(protectedBytes, _entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dataBytes);
            }
            catch
            {
                // If decryption fails, return empty string
                return string.Empty;
            }
        }
    }
}