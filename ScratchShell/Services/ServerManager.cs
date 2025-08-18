using Newtonsoft.Json;
using ScratchShell.Enums;
using ScratchShell.Models;
using ScratchShell.Properties;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ScratchShell.Services;

internal static class ServerManager
{
    internal delegate Task ServerDelegate(Server? server);
    internal delegate Task MangerDelegate();
    internal static event ServerDelegate OnServerAdded;
    internal static event ServerDelegate OnServerRemoved;
    internal static event ServerDelegate OnServerEdited;
    internal static event ServerDelegate OnServerSelected;
    internal static event MangerDelegate OnServerInitialized;
    private static List<Server> _servers;
    internal static Server? SelectedServer { get; private set; }

    // Flag to indicate if servers need to be restored from cloud
    private static bool _needsCloudRestore = false;

    internal static IReadOnlyList<Server> Servers => _servers.AsReadOnly();
    internal static bool NeedsCloudRestore => _needsCloudRestore;
    static ServerManager()
    {
        _servers = new List<Server>();
        var encryptedServers = Settings.Default.Servers;
        if (!string.IsNullOrEmpty(encryptedServers))
        {
            try
            {
                var decryptedServers = EncryptionHelper.Decrypt(encryptedServers);
                var servers = JsonConvert.DeserializeObject<List<Server>>(decryptedServers);
                foreach (var server in servers ?? [])
                {
                    _servers.Add(server);
                }
                System.Diagnostics.Debug.WriteLine($"Successfully loaded {_servers.Count} servers from local storage");
            }
            catch (CryptographicException ex)
            {
                // Encryption keys have changed or data is corrupted
                System.Diagnostics.Debug.WriteLine($"Failed to decrypt existing server data: {ex.Message}");
                System.Diagnostics.Debug.WriteLine("This is expected after logout/login - servers may be restored from cloud sync");

                // Don't clear the encrypted data immediately - cloud sync might restore it
                // Just continue with empty server list for now
                _servers.Clear();

                // Set a flag to indicate that we need cloud sync to recover data
                _needsCloudRestore = true;
            }
            catch (Exception ex)
            {
                // Other decryption or deserialization errors
                System.Diagnostics.Debug.WriteLine($"Error loading servers: {ex.Message}");

                // For other errors, clear the potentially corrupted data
                Settings.Default.Servers = string.Empty;
                Settings.Default.Save();

                // Continue with empty server list
                _servers.Clear();
            }
        }

#if DEBUG
        if (!_servers.Any())
        {
            var random = new Random();

            //for (int i = 0; i < 5; i++)
            //    _servers.Add(
            //        new Server(
            //            $"Server {i + 1}",
            //            $"192.168.1.{random.Next(1, 255)}",
            //            random.Next(22, 65535),
            //            ProtocolType.FTP,
            //            "username",
            //            "password",
            //            random.Next(0, 2) == 0, // Randomly choose to use key file or not
            //            random.Next(0, 2) == 0 ? null : $"C:\\path\\to\\keyfile_{i + 1}.pem", // Random key file path
            //            random.Next(0, 2) == 0 ? null : $"C:\\path\\to\\keyfile_{i + 1}.pem", // Random key file path
            //            random.Next(0, 2) == 0 ? null : "keyfile_password" // Random key file password
            //        )
            //    );
            //var office = new Server(
            //            $"STL-FLAT-APP",
            //            $"202.4.127.189",
            //            2217,
            //            ProtocolType.SSH,
            //            "stl",
            //            "",
            //            true,
            //            "D:\\access-key.pem", // Random key file path
            //            "D:\\access-key.pem", // Random key file path
            //            "" // Random key file password
            //        );
            //_servers.Insert(0, office);
        }
#endif
    }
    internal static void ClearServers()
    {
        _servers.Clear();
        SelectedServer = null;
    }
    internal static void InitializeServers(List<Server> servers)
    {
        _servers.Clear();
        _servers.AddRange(servers);
        SelectedServer = null;

        // If we successfully restored servers, clear the restore flag
        _needsCloudRestore = false;
        OnServerInitialized?.Invoke();
        System.Diagnostics.Debug.WriteLine($"Initialized {servers.Count} servers from cloud sync");
    }

    internal static void RemoveServer(Server serverViewModel)
    {
        _servers.RemoveAll(q => q.Id == serverViewModel.Id);
        SaveSettings();
        OnServerRemoved?.Invoke(serverViewModel);
    }


    internal static void ServerSelected(Server server)
    {
        SelectedServer = _servers.FirstOrDefault(q => q.Id == server.Id);
        OnServerSelected?.Invoke(SelectedServer);
    }

    internal static void ServerEdited(Server server)
    {
        var existingServer = _servers.FirstOrDefault(q => q.Id == server.Id);
        if (existingServer != null)
        {
            existingServer.Name = server.Name;
            existingServer.Host = server.Host;
            existingServer.Port = server.Port;
            existingServer.Username = server.Username;
            existingServer.Password = server.Password;
            existingServer.UseKeyFile = server.UseKeyFile;
            existingServer.PublicKeyFilePath = server.PublicKeyFilePath;
            existingServer.PrivateKeyFilePath = server.PrivateKeyFilePath;
            existingServer.KeyFilePassword = server.KeyFilePassword;
            existingServer.IsDeleted = server.IsDeleted;
            existingServer.ProtocolType = server.ProtocolType;
            SaveSettings();
            OnServerEdited?.Invoke(server);
        }
    }

    internal static void AddServer(Server server)
    {
        _servers.Add(server);
        SaveSettings();
        OnServerAdded?.Invoke(server);
    }

    /// <summary>
    /// Clears old encrypted data after successful cloud restore
    /// </summary>
    internal static void ClearOldEncryptedData()
    {
        if (_needsCloudRestore && _servers.Any())
        {
            // We have successfully restored servers from cloud, so clear the old encrypted data
            System.Diagnostics.Debug.WriteLine("Clearing old encrypted server data after successful cloud restore");
            _needsCloudRestore = false;

            // Save the newly restored servers with current encryption keys
            SaveSettings();
        }
    }

    private static async void SaveSettings()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_servers);
            var encrypted = EncryptionHelper.Encrypt(json);
            Settings.Default.Servers = encrypted;
            Settings.Default.Save();
            var _cloudSyncService = new CloudSyncService(new HttpClient());
            await UserSettingsService.TriggerCloudSyncIfEnabled();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving servers: {ex.Message}");

            // Try to save without encryption as fallback
            try
            {
                var json = JsonConvert.SerializeObject(_servers);
                Settings.Default.Servers = json; // Store unencrypted as last resort
                Settings.Default.Save();
                System.Diagnostics.Debug.WriteLine("Warning: Servers saved without encryption due to encryption failure");
            }
            catch (Exception saveEx)
            {
                System.Diagnostics.Debug.WriteLine($"Critical error: Failed to save servers even without encryption: {saveEx.Message}");
                // Don't throw - just log the error to prevent app crashes
            }
        }
    }
}
