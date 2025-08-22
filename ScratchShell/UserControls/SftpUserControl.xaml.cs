using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Renci.SshNet;
using ScratchShell.UserControls.BrowserControl;
using ScratchShell.ViewModels.Models;
using ScratchShell.Views.Windows;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace ScratchShell.UserControls;

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

        this.Browser = new BrowserUserControl();
        this.BrowserContentControl.Content = this.Browser;
        this.Browser.EnterRequested += BrowserEnterRequested;

        this.Browser.CutRequested += BrowserCutRequested;
        this.Browser.CopyRequested += BrowserCopyRequested;
        this.Browser.PasteRequested += BrowserPasteRequested;
        this.Browser.UploadRequested += BrowserUploadRequested;
        this.Browser.DownloadRequested += BrowserDownloadRequested;

        this.TopToolbar.IsEnabled = false;
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
        ShowProgress(true);
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

        ShowProgress(false);
    }

    private void ShowProgress(bool show)
    {
        TopToolbar.IsEnabled = !show;
        Progress.IsIndeterminate = show;
    }

    private async void ControlLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }
        if (_server == null)
        {
            return;
        }
        // Here you would typically initiate the SSH connection using the server details.
        // For example, you might call a method to connect to the server.
        await ConnectToServer(_server);
        _isInitialized = true;
    }

    private async Task ConnectToServer(ServerViewModel server)
    {
        ShowProgress(true);

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

            Log($"Connected to {server.Name} at {server.Host}:{server.Port}.");
            await GoToFolder("~");
            this.TopToolbar.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log($"Failed to connect to {server.Name}: {ex.Message}");
            return;
        }
        ShowProgress(false);
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
            Log($"Failed to list directory on {_server.Name}: {ex.Message}");
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
        if (_sftpClient is not null)
        {
            this._sftpClient.Disconnect();
            this._sftpClient.Dispose();
        }
    }

    private string? _clipboardPath = null;
    private bool _clipboardIsCut = false;
    private FullScreenWindow _FullScreen;

    public BrowserUserControl Browser { get; }

    private async void BrowserDownloadRequested(BrowserItem item)
    {
        ShowProgress(true);

        if (item.IsFolder)
        {
            // Pick destination folder for entire directory
            var dlg = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Multiselect = false,
                Title = $"Select destination folder for {item.Name}",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {
                try
                {
                    var localPath = System.IO.Path.Combine(dlg.FileName, item.Name);
                    Directory.CreateDirectory(localPath);

                    await Task.Run(() => DownloadDirectory(item.FullPath, localPath));

                    Log($"Downloaded folder {item.Name} to {localPath}");
                }
                catch (Exception ex)
                {
                    Log($"Failed to download folder {item.Name}: {ex.Message}");
                }
            }
        }
        else
        {
            // Save single file
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = item.Name,
                Title = "Download File",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (var fs = new FileStream(saveDialog.FileName, FileMode.Create, FileAccess.Write))
                    {
                        await Task.Run(() => _sftpClient.DownloadFile(item.FullPath, fs));
                    }
                    Log($"Downloaded {item.Name} to {saveDialog.FileName}");
                }
                catch (Exception ex)
                {
                    Log($"Failed to download {item.Name}: {ex.Message}");
                }
            }
        }
        ShowProgress(false);
    }

    /// <summary>
    /// Recursively downloads a remote SFTP directory into a local path.
    /// </summary>
    private void DownloadDirectory(string remotePath, string localPath)
    {
        var files = _sftpClient.ListDirectory(remotePath);

        foreach (var file in files)
        {
            if (file.Name == "." || file.Name == "..")
                continue;

            var localFilePath = System.IO.Path.Combine(localPath, file.Name);
            var remoteFilePath = file.FullName;

            if (file.IsDirectory)
            {
                Directory.CreateDirectory(localFilePath);
                DownloadDirectory(remoteFilePath, localFilePath);
            }
            else
            {
                using (var fs = new FileStream(localFilePath, FileMode.Create, FileAccess.Write))
                {
                    _sftpClient.DownloadFile(remoteFilePath, fs);
                }
            }
        }
    }

    private async void BrowserUploadRequested(BrowserItem item)
    {
        ShowProgress(true);
        OpenFileDialog openDialog = new()
        {
            Title = "Upload File",
            Multiselect = false
        };

        if (openDialog.ShowDialog() == true)
        {
            var localFilePath = openDialog.FileName;
            var remotePath = $"{PathTextBox.Text}/{System.IO.Path.GetFileName(localFilePath)}";

            try
            {
                using (var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read))
                {
                    await Task.Run(() => _sftpClient.UploadFile(fs, remotePath));
                }
                Log($"Uploaded {localFilePath} to {remotePath}");
                await GoToFolder(PathTextBox.Text); // refresh
            }
            catch (Exception ex)
            {
                Log($"Failed to upload {localFilePath}: {ex.Message}");
            }
        }
        ShowProgress(false);
    }

    private void BrowserCopyRequested(BrowserItem item)
    {
        _clipboardPath = item.FullPath;
        _clipboardIsCut = false;
        Log($"Copied {item.Name} to clipboard.");
    }

    private void BrowserCutRequested(BrowserItem item)
    {
        _clipboardPath = item.FullPath;
        _clipboardIsCut = true;
        Log($"Cut {item.Name} to clipboard.");
    }

    private async void BrowserPasteRequested(BrowserItem item)
    {
        ShowProgress(true);
        if (_clipboardPath == null)
        {
            Log("Clipboard is empty.");
            return;
        }

        var fileName = System.IO.Path.GetFileName(_clipboardPath);
        var destinationPath = $"{PathTextBox.Text}/{fileName}";

        try
        {
            // Download temp to memory
            using (var ms = new MemoryStream())
            {
                await Task.Run(() => _sftpClient.DownloadFile(_clipboardPath, ms));
                ms.Position = 0;
                await Task.Run(() => _sftpClient.UploadFile(ms, destinationPath));
            }

            if (_clipboardIsCut)
            {
                await Task.Run(() => _sftpClient.DeleteFile(_clipboardPath));
                Log($"Moved {_clipboardPath} to {destinationPath}");
            }
            else
            {
                Log($"Copied {_clipboardPath} to {destinationPath}");
            }

            _clipboardPath = null;
            await GoToFolder(PathTextBox.Text); // refresh
        }
        catch (Exception ex)
        {
            Log($"Paste failed: {ex.Message}");
        }
        ShowProgress(false);
    }

    private void Log(string message)
    {
        Terminal.Text = Terminal.Text + message + "\n";
    }

    private void LogToggleButtonChecked(object sender, RoutedEventArgs e)
    {
        LogGrid.Visibility = Visibility.Visible;
    }

    private void LogToggleButtonUnChecked(object sender, RoutedEventArgs e)
    {
        LogGrid.Visibility = Visibility.Collapsed;
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        FullScreenButton.IsEnabled = false;
        BrowserContentControl.Content = null;
        _FullScreen = new FullScreenWindow(this.Browser, _server.Name);
        _FullScreen.Show();
        _FullScreen.Closed += (s, args) =>
        {
            // Reinitialize the terminal when exiting full screen
            _FullScreen.RootContentDialog.Content = null;

            BrowserContentControl.Content = this.Browser;
            _FullScreen = null;
            FullScreenButton.IsEnabled = true;
        };
    }
}