using ScratchShell.Constants;
using ScratchShell.Enums;
using ScratchShell.Models;
using ScratchShell.Services;
using ScratchShell.Properties;
using ScratchShell.View.Dialog;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace ScratchShell.ViewModels.Models
{
    public partial class ServerViewModel : ObservableObject
    {

        [ObservableProperty]
        private IEnumerable<ProtocolType> _protocolTypes;

        [ObservableProperty]
        private string _id;

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _host;

        [ObservableProperty]
        private int _port;

        [ObservableProperty]
        private ProtocolType _protocolType;

        [ObservableProperty]
        private string _username;

        [ObservableProperty]
        private string _password;

        [ObservableProperty]
        private bool _useKeyFile;

        [ObservableProperty]
        private string _publicKeyFilePath;

        [ObservableProperty]
        private string _privateKeyFilePath;


        [ObservableProperty]
        private string _keyFilePassword;

        [ObservableProperty]
        private bool _isDeleted;

        public IContentDialogService ContentDialogService { get; }

        public ServerViewModel(IContentDialogService contentDialogService)
        {
            ContentDialogService = contentDialogService;
            ProtocolTypes = Enum.GetValues(typeof(ProtocolType)).Cast<ProtocolType>();
        }

        public ServerViewModel(Server server, IContentDialogService contentDialogService)
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

        internal Server ToServer()
        {
            var server = new Server(
                Id,
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
                Server = this,
            };
            return item;
        }
    }
}