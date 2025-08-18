using Newtonsoft.Json;
using ScratchShell.Enums;
using ScratchShell.Models;
using ScratchShell.Properties;
using System.Threading.Tasks;

namespace ScratchShell.Services;

internal static class ServerManager
{
    internal delegate Task ServerDelegate(Server? server);
    internal static event ServerDelegate OnServerAdded;
    internal static event ServerDelegate OnServerRemoved;
    internal static event ServerDelegate OnServerEdited;
    internal static event ServerDelegate OnServerSelected;
    private static List<Server> _servers;
    internal static Server? SelectedServer { get; private set; }

    internal static IReadOnlyList<Server> Servers => _servers.AsReadOnly();
    static ServerManager()
    {
        _servers = new List<Server>();
        var encryptedServers = Settings.Default.Servers;
        if (!string.IsNullOrEmpty(encryptedServers))
        {
            var decryptedServers = EncryptionHelper.Decrypt(encryptedServers);
            var servers = JsonConvert.DeserializeObject<List<Server>>(decryptedServers);
            foreach (var server in servers ?? [])
            {
                _servers.Add(server);
            }
        }

#if DEBUG
        if(!_servers.Any())
        {
            var random = new Random();

            for (int i = 0; i < 5; i++)
                _servers.Add(
                    new Server(
                        $"Server {i + 1}",
                        $"192.168.1.{random.Next(1, 255)}",
                        random.Next(22, 65535),
                        ProtocolType.FTP,
                        "username",
                        "password",
                        random.Next(0, 2) == 0, // Randomly choose to use key file or not
                        random.Next(0, 2) == 0 ? null : $"C:\\path\\to\\keyfile_{i + 1}.pem", // Random key file path
                        random.Next(0, 2) == 0 ? null : $"C:\\path\\to\\keyfile_{i + 1}.pem", // Random key file path
                        random.Next(0, 2) == 0 ? null : "keyfile_password" // Random key file password
                    )
                );
            var office = new Server(
                        $"STL-FLAT-APP",
                        $"202.4.127.189",
                        2217,
                        ProtocolType.SSH,
                        "stl",
                        "",
                        true,
                        "D:\\access-key.pem", // Random key file path
                        "D:\\access-key.pem", // Random key file path
                        "" // Random key file password
                    );
            _servers.Insert(0, office);
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
    private static void SaveSettings()
    {
        var json = JsonConvert.SerializeObject(_servers);
        var encrypted = EncryptionHelper.Encrypt(json);
        Settings.Default.Servers = encrypted;
        Settings.Default.Save();
    }
}
