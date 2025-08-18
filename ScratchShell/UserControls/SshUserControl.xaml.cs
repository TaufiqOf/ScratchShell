using Renci.SshNet;
using ScratchShell.ViewModels.Models;
using ScratchShell.UserControls.TerminalControl;
using ScratchShell.ViewModels.Models;
using System.Text;
using System.Windows.Controls;
using System.Reflection;


namespace ScratchShell.UserControls
{
    /// <summary>
    /// Interaction logic for SshUserControl.xaml
    /// </summary>
    public partial class SshUserControl : UserControl, IWorkspaceControl
    {
        private ServerViewModel _server;
        private SshClient? _sshClient;
        private ShellStream _shellStream;
        private bool _isInitialized = false;

        public SshUserControl(ServerViewModel server)
        {
            InitializeComponent();
            this._server = server;
            Terminal.InputLineSyntax = "";
            Terminal.CommandEntered += TerminalCommandEntered;
            Terminal.SizeChanged += TerminalSizeChanged;
            this.Loaded += ControlLoaded;
            
        }

        private void TerminalSizeChanged(TerminalUserControl terminal, Size newSize)
        {
            if (_shellStream != null && _sshClient != null && _sshClient.IsConnected)
            {
                var cols = (uint)Math.Max(1, newSize.Width / 8);
                var rows = (uint)Math.Max(1, newSize.Height / 16);
                var pixelWidth = (uint)newSize.Width;
                var pixelHeight = (uint)newSize.Height;
                
                try
                {
                    // Use reflection to access the internal _channel field
                    var channelField = _shellStream.GetType()
                        .GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance);
                    var channel = channelField?.GetValue(_shellStream);
                    
                    // Call SendWindowChangeRequest on the channel
                    var method = channel?.GetType()
                        .GetMethod("SendWindowChangeRequest", BindingFlags.Public | BindingFlags.Instance);
                    method?.Invoke(channel, new object[] { cols, rows, pixelWidth, pixelHeight });
                }
                catch (Exception ex)
                {
                    // Log or handle the error if reflection fails
                    Terminal.AddOutput($"Failed to resize terminal: {ex.Message}");
                }
            }
        }

        private async void ControlLoaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
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
            Terminal.IsReadOnly = true;
            Progress.IsIndeterminate = true;
            if (server.UseKeyFile)
            {
                // Use key file authentication
                var privateKey = new PrivateKeyFile(server.PrivateKeyFilePath, server.KeyFilePassword);
                var keyFiles = new[] { privateKey };
                var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, new PrivateKeyAuthenticationMethod(server.Username, keyFiles));
                _sshClient = new SshClient(connectionInfo);
            }
            else
            {
                // Use password authentication
                var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, new PasswordAuthenticationMethod(server.Username, server.Password));
                _sshClient = new SshClient(connectionInfo);
            }
            try
            {
                await _sshClient.ConnectAsync(CancellationToken.None);
                _shellStream = _sshClient.CreateShellStream("vt100", 80, 24, 0, 0, 4096);

                Terminal.AddOutput($"Connected to {server.Name} at {server.Host}:{server.Port}.");
                StartReadLoop();
            }
            catch (Exception ex)
            {
                Terminal.AddOutput($"Failed to connect to {server.Name}: {ex.Message}");
                return;
            }
            Terminal.IsReadOnly = false;
            Progress.IsIndeterminate = false;
        }

        private async Task StartReadLoop()
        {
            await Task.Run(() =>
            {
                var outputBuffer = new StringBuilder();

                while (_sshClient is not null && _sshClient.IsConnected)
                {
                    string output = _shellStream.Read();
                    if (!string.IsNullOrEmpty(output))
                    {
                        outputBuffer.Append(output);
                        Application.Current.Dispatcher.Invoke(async () =>
                        {
                            await Task.Delay(1000); // Small delay to allow UI to update
                            Terminal.AddOutput(outputBuffer.ToString());
                        });
                    }
                }
            });
        }

        private void TerminalCommandEntered(TerminalUserControl sender, string command)
        {
            try
            {
                _shellStream.WriteLine(command);
            }
            catch (Exception ex)
            {
                Terminal.AddOutput("Error: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_sshClient is not null)
            {
                this._sshClient.Disconnect();
                this._sshClient.Dispose();
            }
        }
    }
}
