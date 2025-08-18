using ScratchShell.Properties;
using System.Security.Cryptography;
using System.Text;

namespace ScratchShell.Services
{
    public class UserSettingsService
    {
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("ScratchShellEntropy2024");

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
        public static void StoreAuthenticationCredentials(string token, string username, bool rememberMe = true)
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
        public static void ClearStoredCredentials()
        {
            Settings.Default.AuthToken = string.Empty;
            Settings.Default.Username = string.Empty;
            Settings.Default.RememberMe = false;
            Settings.Default.Save();

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
        public static void SetRememberMe(bool rememberMe)
        {
            Settings.Default.RememberMe = rememberMe;
            Settings.Default.Save();

            // If remember me is disabled, clear the stored token
            if (!rememberMe)
            {
                Settings.Default.AuthToken = string.Empty;
                Settings.Default.Save();
            }
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