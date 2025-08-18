using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ScratchShell.Services;

public static class SecureKeyStore
{
    private static readonly string AppFolder = Path.Combine(CommonService.GetUserPath(), "Secure");
    private static readonly string KeyPath = Path.Combine(AppFolder, "aes.key");
    private static readonly string IvPath = Path.Combine(AppFolder, "aes.iv");

    public static byte[] Key { get; private set; }
    public static byte[] IV { get; private set; }

    private static string? _currentUserContext = null;

    static SecureKeyStore()
    {
        Directory.CreateDirectory(AppFolder);
        InitializeWithLocalKeys();
    }
    public static void ResetKey()
    {
        File.Delete(KeyPath);
        File.Delete(IvPath);
    }
    /// <summary>
    /// Initializes encryption keys using local device-specific keys (for local data only)
    /// </summary>
    private static void InitializeWithLocalKeys()
    {
        try
        {
            if (File.Exists(KeyPath) && File.Exists(IvPath))
            {
                Key = Decrypt(File.ReadAllBytes(KeyPath));
                IV = Decrypt(File.ReadAllBytes(IvPath));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing local encryption keys: {ex.Message}");

        }
        finally
        {

        }
    }

    /// <summary>
    /// Sets up encryption keys based on user credentials for cross-device compatibility
    /// </summary>
    /// <param name="username">User's username/email</param>
    /// <param name="password">User's password (will be cleared from memory)</param>
    public static void InitializeForUser(string username)
    {
        try
        {
            // Create a consistent salt based on username
            var salt = Encoding.UTF8.GetBytes($"ScratchShell_Salt_{username}");

            // Derive key and IV from password using PBKDF2
            using var pbkdf2 = new Rfc2898DeriveBytes("{7AFF965F-9610-4148-BA6D-0BC5B899C4F1}", salt, 100000, HashAlgorithmName.SHA256);

            Key = pbkdf2.GetBytes(32); // 256-bit key
            IV = pbkdf2.GetBytes(16);  // 128-bit IV

            _currentUserContext = username;

            File.WriteAllBytes(KeyPath, Encrypt(Key));
            File.WriteAllBytes(IvPath, Encrypt(IV));
            InitializeWithLocalKeys();
            // Clear password from memory (though this doesn't guarantee it's scrubbed)
            // In production, consider using SecureString or other secure memory techniques
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing user encryption keys: {ex.Message}");
            // Fallback to local keys if user key derivation fails
        }
    }

    /// <summary>
    /// Clears user-specific encryption keys and reverts to local keys
    /// </summary>
    public static void ClearUserKeys()
    {
        _currentUserContext = null;
        InitializeWithLocalKeys();
    }

    /// <summary>
    /// Gets encryption keys for cloud sync (user-derived keys)
    /// </summary>
    /// <returns>Tuple of (Key, IV) for cloud encryption, or null if no user context</returns>
    public static (byte[] Key, byte[] IV)? GetCloudEncryptionKeys()
    {
        if (_currentUserContext != null)
        {
            return (Key, IV);
        }
        return null;
    }

    /// <summary>
    /// Checks if user-derived keys are available for cloud sync
    /// </summary>
    public static bool HasUserKeys => _currentUserContext != null;

    private static byte[] Encrypt(byte[] data)
    {
        return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
    }

    private static byte[] Decrypt(byte[] data)
    {
        return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
    }

    
}