using ScratchShell.Constants;
using ScratchShell.Enums;
using ScratchShell.Models;
using ScratchShell.Properties;
using ScratchShell.Services;
using ScratchShell.UserControls;
using ScratchShell.View.Dialog;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace ScratchShell.ViewModels.Models
{
    public partial class ServerViewModel : ObservableValidator, IDataErrorInfo
    {


        [ObservableProperty]
        private IEnumerable<ProtocolType> _protocolTypes;

        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _host = string.Empty;

        [ObservableProperty]
        private int _port = 22;

        [ObservableProperty]
        private ProtocolType _protocolType = ProtocolType.SSH;

        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private bool _useKeyFile;

        [ObservableProperty]
        private string _publicKeyFilePath = string.Empty;

        [ObservableProperty]
        private string _privateKeyFilePath = string.Empty;

        [ObservableProperty]
        private string _keyFilePassword = string.Empty;

        [ObservableProperty]
        private bool _isDeleted;

        public IContentDialogService ContentDialogService { get; }

        // Validation properties
        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(Name):
                        return ValidateName();

                    case nameof(Host):
                        return ValidateHost();

                    case nameof(Port):
                        return ValidatePort();

                    case nameof(Username):
                        return ValidateUsername();

                    default:
                        return string.Empty;
                }
            }
        }

        public bool IsValid
        {
            get
            {
                return string.IsNullOrEmpty(ValidateName()) &&
                       string.IsNullOrEmpty(ValidateHost()) &&
                       string.IsNullOrEmpty(ValidatePort()) &&
                       string.IsNullOrEmpty(ValidateUsername());
            }
        }

        private string ValidateName()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return "Server name is required.";

            if (Name.Length > 100)
                return "Server name cannot exceed 100 characters.";

            // Check for invalid characters
            var invalidChars = new char[] { '<', '>', ':', '"', '|', '?', '*', '\\', '/' };
            if (Name.IndexOfAny(invalidChars) >= 0)
                return "Server name contains invalid characters.";

            return string.Empty;
        }

        private string ValidateHost()
        {
            if (string.IsNullOrWhiteSpace(Host))
                return "Host is required.";

            // Trim whitespace
            var hostToValidate = Host.Trim();

            // Check if it's a valid IP address
            if (IPAddress.TryParse(hostToValidate, out _))
                return string.Empty;

            // Check if it's a valid hostname/domain
            if (Uri.CheckHostName(hostToValidate) == UriHostNameType.Dns ||
                Uri.CheckHostName(hostToValidate) == UriHostNameType.IPv4 ||
                Uri.CheckHostName(hostToValidate) == UriHostNameType.IPv6)
                return string.Empty;

            return "Please enter a valid IP address or hostname.";
        }

        private string ValidatePort()
        {
            if (Port <= 0 || Port > 65535)
                return "Port must be between 1 and 65535.";

            return string.Empty;
        }

        private string ValidateUsername()
        {
            if (string.IsNullOrWhiteSpace(Username))
                return "Username is required.";

            if (Username.Length > 255)
                return "Username cannot exceed 255 characters.";

            return string.Empty;
        }

        public ServerViewModel(IContentDialogService contentDialogService)
        {
            ContentDialogService = contentDialogService;
            ProtocolTypes = Enum.GetValues(typeof(ProtocolType)).Cast<ProtocolType>();

            // Set default port based on protocol
            SetDefaultPortForProtocol();
        }

        public ServerViewModel(Server server, IContentDialogService contentDialogService)
        {
            ProtocolTypes = Enum.GetValues(typeof(ProtocolType)).Cast<ProtocolType>();

            Id = server.Id;
            Name = server.Name ?? string.Empty;
            Host = server.Host ?? string.Empty;
            Port = server.Port;
            ProtocolType = server.ProtocolType;
            Username = server.Username ?? string.Empty;
            Password = server.Password ?? string.Empty;
            UseKeyFile = server.UseKeyFile;
            PublicKeyFilePath = server.PublicKeyFilePath ?? string.Empty;
            PrivateKeyFilePath = server.PrivateKeyFilePath ?? string.Empty;
            KeyFilePassword = server.KeyFilePassword ?? string.Empty;
            IsDeleted = server.IsDeleted;
            ContentDialogService = contentDialogService;
        }

        public ServerViewModel(ServerViewModel server, IContentDialogService contentDialogService)
        {
            ProtocolTypes = Enum.GetValues(typeof(ProtocolType)).Cast<ProtocolType>();

            Id = server.Id;
            Name = server.Name;
            Host = server.Host;
            Port = server.Port;
            ProtocolType = server.ProtocolType;
            Username = server.Username;
            Password = server.Password;
            UseKeyFile = server.UseKeyFile;
            PublicKeyFilePath = server.PublicKeyFilePath;
            PrivateKeyFilePath = server.PrivateKeyFilePath;
            KeyFilePassword = server.KeyFilePassword;
            IsDeleted = server.IsDeleted;
            ContentDialogService = contentDialogService;
        }

        partial void OnProtocolTypeChanged(ProtocolType value)
        {
            SetDefaultPortForProtocol();
        }

        private void SetDefaultPortForProtocol()
        {
            // Only set default port if current port is a default port for another protocol
            // or if port is 0
            if (Port == 0 || Port == 21 || Port == 22 || Port == 23)
            {
                switch (ProtocolType)
                {
                    case ProtocolType.SSH:
                        Port = 22;
                        break;

                    case ProtocolType.FTP:
                        Port = 21;
                        break;

                    case ProtocolType.SFTP:
                        Port = 22;
                        break;
                }
            }
        }

        [RelayCommand]
        private async Task OnCopyServer()
        {
            var newServer = this.ToServer();
            newServer.Id = Guid.NewGuid().ToString();
            newServer.Name += " (Copy)";
            newServer.IsDeleted = false;
            ServerManager.AddServer(newServer);
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task OnEditServer()
        {
            if (ContentDialogService is not null)
            {
                var termsOfUseContentDialog = new ServerContentDialog(ContentDialogService.GetDialogHost(), this);

                var result = await termsOfUseContentDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    ServerManager.ServerEdited(this.ToServer());
                }
            }
        }

        [RelayCommand]
        private async Task OnOpenTerminal(string? mode)
        {
            bool runAsAdmin = string.Equals(mode, TerminalConstant.Elevated, StringComparison.OrdinalIgnoreCase);

            try
            {
                var shellType = CommonService.GetEnumValue<ShellType>(Settings.Default.DefaultShellType);
                TerminalService.Launch(this.ProtocolType.ToString(), this, shellType, runAsAdmin, ApplicationConstant.Name);
            }
            catch (NotSupportedException ex)
            {
                await ContentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
                {
                    Title = "Error",
                    Content = ex.Message,
                    CloseButtonText = "OK"
                });
            }
            catch (Win32Exception)
            {
                await ContentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
                {
                    Title = "Cancelled",
                    Content = "Terminal was not opened (admin permission was declined).",
                    CloseButtonText = "OK"
                });
            }
        }

        [RelayCommand]
        private async Task OnDeleteServer()
        {
            if (ContentDialogService is not null)
            {
                ContentDialogResult result = await ContentDialogService.ShowSimpleDialogAsync(
                        new SimpleContentDialogCreateOptions()
                        {
                            Title = "Delete this server?",
                            Content = $"Are you sure you want to delete {Name}?",
                            PrimaryButtonText = "Delete",
                            CloseButtonText = "Cancel",
                        }
                    );
                if (result == ContentDialogResult.Primary)
                {
                    await DeleteServer();
                }
            }
        }

        [RelayCommand]
        private async Task OnEnterServer()
        {
            ServerManager.ServerSelected(this.ToServer());
        }

        private async Task? DeleteServer()
        {
            IsDeleted = true;
            ServerManager.RemoveServer(this.ToServer());
        }

        internal Server ToServer(bool setNewIds = false)
        {
            var id = Id;
            if (setNewIds)
            {
                id = Guid.NewGuid().ToString();
            }
            var server = new Server(
                id,
                Name,
                Host,
                Port,
                ProtocolType,
                Username,
                Password,
                UseKeyFile,
                PublicKeyFilePath,
                PrivateKeyFilePath,
                KeyFilePassword
            )
            {
                IsDeleted = IsDeleted
            };
            return server;
        }

        internal TabItemViewModel ToTabItemViewModel()
        {
            var item = new TabItemViewModel()
            {
                ContentDialogService = ContentDialogService,
                Server = this,
            };
            return item;
        }
    }
}