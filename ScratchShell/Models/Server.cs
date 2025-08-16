
using ScratchShell.Enums;

namespace ScratchShell.Models;
public class Server
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Host { get; set; }
    public int Port { get; set; }
    public ProtocolType ProtocolType { get; set; } = ProtocolType.SSH;

    public string Username { get; set; }
    public string Password { get; set; }
    public bool UseKeyFile { get; set; }
    public string PublicKeyFilePath { get; set; }
    public string PrivateKeyFilePath { get; set; }
    public string KeyFilePassword { get; set; }

    public bool IsDeleted { get; set; }
    public Server()
    {

    }

    public Server(string name, string host, int port, ProtocolType protocolType, string username, string password, bool useKeyFile = false, string publicKeyFilePath = null,string privateKeyFilePath= null, string keyFilePassword = null)
    {
        Id = Guid.NewGuid().ToString();
        Name = name;
        Host = host;
        Port = port;
        ProtocolType = protocolType;
        Username = username;
        Password = password;
        UseKeyFile = useKeyFile;
        PublicKeyFilePath = publicKeyFilePath;
        PrivateKeyFilePath = privateKeyFilePath;
        KeyFilePassword = keyFilePassword;
    }

    public Server(string id, string name, string host, int port, ProtocolType protocolType, string username, string password, bool useKeyFile = false, string publicKeyFilePath = null, string privateKeyFilePath = null, string keyFilePassword = null)
    {
        Id = id;
        Name = name;
        Host = host;
        Port = port;
        ProtocolType = protocolType;
        Username = username;
        Password = password;
        UseKeyFile = useKeyFile;
        PublicKeyFilePath = publicKeyFilePath;
        PrivateKeyFilePath = privateKeyFilePath;
        KeyFilePassword = keyFilePassword;
    }
}
