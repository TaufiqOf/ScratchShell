using Renci.SshNet;
using ScratchShell.Models;
using ScratchShell.UserControls.BrowserControl;
using ScratchShell.ViewModels.Models;
using ScratchShell.ViewModels.Models;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;


namespace ScratchShell.UserControls
{
    /// <summary>
    /// Interaction logic for SshUserControl.xaml
    /// </summary>
    public partial class SftpUserControl : UserControl, IWorkspaceControl
    {
        private ServerViewModel _server;
        private SftpClient? _sftpClient;
        private ShellStream _shellStream;
        private bool _isInitialized = false;
        public SftpUserControl(ServerViewModel server)
        {
            InitializeComponent();
            this._server = server;
            this.Loaded += ControlLoaded;
            this.Browser.EnterRequested += BrowserEnterRequested;
            
            this.Browser.CutRequested += BrowserCutRequested;
            this.Browser.CopyRequested += BrowserCopyRequested;
            this.Browser.PasteRequested += BrowserPasteRequested;
            this.Browser.UploadRequested += BrowserUploadRequested;
            this.Browser.DownloadRequested += BrowserDownloadRequested;

            this.TopToolbar.IsEnabled = false;
        }

        private void BrowserDownloadRequested(BrowserItem item)
        {
            throw new NotImplementedException();
        }

        private void BrowserUploadRequested(BrowserItem item)
        {
            throw new NotImplementedException();
        }

        private void BrowserPasteRequested(BrowserItem item)
        {
            throw new NotImplementedException();
        }

        private void BrowserCutRequested(BrowserItem item)
        {
            throw new NotImplementedException();
        }

        private void BrowserCopyRequested(BrowserItem obj)
        {
            throw new NotImplementedException();
        }

        private async void BrowserEnterRequested(BrowserItem obj)
        {
            if (obj.IsFolder)
            {
                await GoToFolder(obj.FullPath);
            }
        }

        private async Task GoToFolder(string path)
        {
            this.TopToolbar.IsEnabled = false;
            Progress.IsIndeterminate = true;
            Browser.Clear();
            PathTextBox.Text = path;

            // Parent folder always added first
            Browser.AddItem(new BrowserItem
            {
                Name = "..",
                FullPath = $"{path}/..",
                IsFolder = true,
                LastUpdated = DateTime.Now,
                Size = 0
            });

            // Stream items one by one
            await foreach (var item in FileDriveControlGetDirectory(path))
            {
                Browser.AddItem(item); // Incremental UI updates
            }

            this.TopToolbar.IsEnabled = true;
            Progress.IsIndeterminate = false;
        }


        private async void ControlLoaded(object sender, RoutedEventArgs e)
        {
            if(_isInitialized)
            {
                return;
            }
            if (_server == null)
            {
                Terminal.AddOutput("Server is not initialized.");
                return;
            }
            Terminal.AddOutput($"Connecting to {_server.Name} at {_server.Host}:{_server.Port} using {_server.ProtocolType} protocol.");
            // Here you would typically initiate the SSH connection using the server details.
            // For example, you might call a method to connect to the server.
            await ConnectToServer(_server);
            _isInitialized = true;
        }

        private async Task ConnectToServer(ServerViewModel server)
        {
            this.TopToolbar.IsEnabled = false;
            Progress.IsIndeterminate = true;
            if (server.UseKeyFile)
            {
                // Use key file authentication
                var privateKey = new PrivateKeyFile(server.PrivateKeyFilePath, server.KeyFilePassword);
                var keyFiles = new[] { privateKey };
                var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, new PrivateKeyAuthenticationMethod(server.Username, keyFiles));
                _sftpClient = new SftpClient(connectionInfo);
            }
            else
            {
                // Use password authentication
                var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, new PasswordAuthenticationMethod(server.Username, server.Password));
                _sftpClient = new SftpClient(connectionInfo);
            }
            try
            {
                await _sftpClient.ConnectAsync(CancellationToken.None);

                Terminal.AddOutput($"Connected to {server.Name} at {server.Host}:{server.Port}.");
                await GoToFolder("~");
                this.TopToolbar.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Terminal.AddOutput($"Failed to connect to {server.Name}: {ex.Message}");
                return;
            }
            this.TopToolbar.IsEnabled = true;
            Progress.IsIndeterminate = false;
        }
        private async IAsyncEnumerable<BrowserItem> FileDriveControlGetDirectory(
         string path,
         [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            path = path.Replace("~", _sftpClient.WorkingDirectory);

            IAsyncEnumerable<Renci.SshNet.Sftp.ISftpFile> dirStream;

            try
            {
                dirStream = _sftpClient.ListDirectoryAsync(path, cancellationToken);
            }
            catch (Exception ex)
            {
                Terminal.AddOutput($"Failed to list directory on {_server.Name}: {ex.Message}");
                yield break; // 👈 stop enumeration cleanly
            }

            await foreach (var item in dirStream.WithCancellation(cancellationToken))
            {
                if (item.Name == "." || item.Name == "..")
                    continue;

                var fileItem = new BrowserItem
                {
                    Name = item.Name,
                    FullPath = item.FullName,
                    LastUpdated = item.LastWriteTime,
                    IsFolder = item.IsDirectory,
                    Size = item.IsDirectory ? 0 : item.Attributes.Size,
                };

                yield return fileItem; // 👈 now legal
            }
        }


        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await GoToFolder(PathTextBox.Text);
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            await GoToFolder($"{PathTextBox.Text}/..");
        }

        private async void PathTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                await GoToFolder(PathTextBox.Text);
            }
        }

        public void Dispose()
        {
            if(_sftpClient is not null)
            {
                this._sftpClient.Disconnect();
                this._sftpClient.Dispose();
            }
        }
    }
}
