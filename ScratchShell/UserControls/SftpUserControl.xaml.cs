using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Renci.SshNet;
using ScratchShell.Models;
using ScratchShell.Services;
using ScratchShell.UserControls.BrowserControl;
using ScratchShell.ViewModels.Models;
using ScratchShell.Views.Windows;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace ScratchShell.UserControls;

/// <summary>
/// Interaction logic for SftpUserControl.xaml
/// </summary>
public partial class SftpUserControl : UserControl, IWorkspaceControl
{
    private readonly ServerViewModel _server;
    private readonly IContentDialogService _contentDialogService;
    private SftpClient? _sftpClient;
    private ISftpFileOperationService? _fileOperationService;
    private bool _isInitialized = false;

    public BrowserUserControl Browser { get; }

    public SftpUserControl(ServerViewModel server, IContentDialogService contentDialogService)
    {
        InitializeComponent();
        _server = server;
        _contentDialogService = contentDialogService;
        
        Browser = new BrowserUserControl();
        BrowserContentControl.Content = Browser;
        
        this.Loaded += ControlLoaded;
        this.TopToolbar.IsEnabled = false;
        this.KeyDown += SftpUserControl_KeyDown;
        
        SetupBrowserEvents();
    }

    private void SetupBrowserEvents()
    {
        Browser.EnterRequested += BrowserEnterRequested;
        Browser.ItemRenamed += BrowserItemRenamed;
        Browser.NewFolderCreated += BrowserNewFolderCreated;
        Browser.ItemEditCancelled += BrowserItemEditCancelled;
        
        // Use the new refactored operation methods
        Browser.CutRequested += item => HandleFileOperation(BrowserOperationContext.ForItem(item, PathTextBox.Text), FileOperationType.Cut);
        Browser.CopyRequested += item => HandleFileOperation(BrowserOperationContext.ForItem(item, PathTextBox.Text), FileOperationType.Copy);
        Browser.PasteRequested += item => HandleFileOperation(BrowserOperationContext.ForItem(item, PathTextBox.Text), FileOperationType.Paste);
        Browser.UploadRequested += item => HandleFileOperation(BrowserOperationContext.ForItem(item, PathTextBox.Text), FileOperationType.Upload);
        Browser.DownloadRequested += item => HandleFileOperation(BrowserOperationContext.ForItem(item, PathTextBox.Text), FileOperationType.Download);
        Browser.DeleteRequested += item => HandleDeleteWithConfirmation(item);
        
        // Empty space operations
        Browser.EmptySpacePasteRequested += () => HandleFileOperation(BrowserOperationContext.ForDirectory(PathTextBox.Text), FileOperationType.Paste);
        Browser.EmptySpaceUploadRequested += () => HandleFileOperation(BrowserOperationContext.ForDirectory(PathTextBox.Text), FileOperationType.Upload);
        Browser.EmptySpaceNewFolderRequested += () => HandleFileOperation(BrowserOperationContext.ForDirectory(PathTextBox.Text), FileOperationType.NewFolder);
        
        // Multi-select operations
        Browser.MultiCopyRequested += HandleMultiCopyOperation;
        Browser.MultiCutRequested += HandleMultiCutOperation;
        Browser.MultiDeleteRequested += HandleMultiDeleteWithConfirmation;
        
        // Selection change
        Browser.SelectionChanged += OnBrowserSelectionChanged;
    }

    private async void HandleFileOperation(BrowserOperationContext context, FileOperationType operationType)
    {
        if (_fileOperationService == null)
        {
            Log("❌ File operation service not available");
            return;
        }

        Log($"🔄 {operationType} operation requested for: {context.GetDisplayName()}");

        try
        {
            var result = operationType switch
            {
                FileOperationType.Cut => HandleCutOperation(context),
                FileOperationType.Copy => HandleCopyOperation(context),
                FileOperationType.Paste => await HandlePasteOperation(context),
                FileOperationType.Upload => await HandleUploadOperation(context),
                FileOperationType.Download => await HandleDownloadOperation(context),
                FileOperationType.NewFolder => await HandleNewFolderOperation(context),
                FileOperationType.Delete => await HandleDeleteOperation(context),
                _ => throw new ArgumentOutOfRangeException(nameof(operationType), operationType, null)
            };

            if (result != null && !result.IsSuccess)
            {
                Log($"❌ Operation failed: {result.ErrorMessage}");
            }
            else if (operationType == FileOperationType.Paste || 
                     operationType == FileOperationType.Upload || 
                     operationType == FileOperationType.NewFolder ||
                     operationType == FileOperationType.Delete)
            {
                // Refresh directory for operations that modify the file system
                Log($"🔄 Refreshing directory to show changes");
                await GoToFolder(PathTextBox.Text);
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Unexpected error during {operationType} operation: {ex.Message}");
        }
    }

    private OperationResult HandleCutOperation(BrowserOperationContext context)
    {
        if (context.TargetItem == null) return OperationResult.Failure("No item selected");
        
        _fileOperationService?.UpdateClipboard(context.TargetItem.FullPath, true);
        return OperationResult.Success();
    }

    private OperationResult HandleCopyOperation(BrowserOperationContext context)
    {
        if (context.TargetItem == null) return OperationResult.Failure("No item selected");
        
        _fileOperationService?.UpdateClipboard(context.TargetItem.FullPath, false);
        return OperationResult.Success();
    }

    private async Task<OperationResult> HandlePasteOperation(BrowserOperationContext context)
    {
        if (!_fileOperationService?.HasClipboardContent == true)
        {
            return OperationResult.Failure("Clipboard is empty");
        }

        return await _fileOperationService.PasteItemAsync(context.CurrentDirectory);
    }

    private async Task<OperationResult> HandleUploadOperation(BrowserOperationContext context)
    {
        var openDialog = new OpenFileDialog
        {
            Title = "Upload File",
            Multiselect = false
        };

        if (openDialog.ShowDialog() == true)
        {
            var localFilePath = openDialog.FileName;
            var remotePath = $"{context.CurrentDirectory}/{Path.GetFileName(localFilePath)}";
            
            return await _fileOperationService!.UploadFileAsync(localFilePath, remotePath);
        }

        Log("🚫 Upload cancelled by user");
        return OperationResult.Success(); // User cancellation is not an error
    }

    private async Task<OperationResult> HandleDownloadOperation(BrowserOperationContext context)
    {
        if (context.TargetItem == null) return OperationResult.Failure("No item selected");

        Log($"⬇️ Download requested for: {context.TargetItem.Name}");
        ShowProgress(true, $"Downloading {context.TargetItem.Name}...");

        try
        {
            if (context.TargetItem.IsFolder)
            {
                return await HandleFolderDownload(context.TargetItem);
            }
            else
            {
                return await HandleFileDownload(context.TargetItem);
            }
        }
        finally
        {
            ShowProgress(false);
        }
    }

    private async Task<OperationResult> HandleNewFolderOperation(BrowserOperationContext context)
    {
        Browser.StartNewFolderCreation();
        return OperationResult.Success();
    }

    private async Task<OperationResult> HandleDeleteOperation(BrowserOperationContext context)
    {
        if (context.TargetItem == null) return OperationResult.Failure("No item selected");

        return await _fileOperationService!.DeleteItemAsync(context.TargetItem.FullPath);
    }

    private void HandleMultiSelectOperation(FileOperationType operationType)
    {
        var selectedItems = Browser.GetSelectedItems();
        
        if (!selectedItems.Any())
        {
            Log($"❌ {operationType} operation failed: No items selected");
            return;
        }

        // Filter out parent directory (..) entries
        var validItems = selectedItems.Where(item => item.Name != "..").ToList();
        
        if (!validItems.Any())
        {
            Log($"❌ {operationType} operation failed: No valid items selected");
            return;
        }

        Log($"🔄 {operationType} operation requested for {validItems.Count} selected item(s)");

        try
        {
            switch (operationType)
            {
                case FileOperationType.Copy:
                    HandleMultiCopyOperation(validItems);
                    break;
                case FileOperationType.Cut:
                    HandleMultiCutOperation(validItems);
                    break;
                case FileOperationType.Delete:
                    HandleMultiDeleteOperation(validItems);
                    break;
                default:
                    Log($"❌ Multi-select operation not supported for: {operationType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Unexpected error during {operationType} operation: {ex.Message}");
        }
    }

    private void HandleMultiCopyOperation(List<BrowserItem> items)
    {
        var itemPaths = items.Select(item => item.FullPath).ToList();
        _fileOperationService?.UpdateMultiClipboard(itemPaths, false);
        
        var itemNames = string.Join(", ", items.Select(item => item.Name));
        Log($"📋 Copied {items.Count} item(s) to clipboard: {itemNames}");
    }

    private void HandleMultiCutOperation(List<BrowserItem> items)
    {
        var itemPaths = items.Select(item => item.FullPath).ToList();
        _fileOperationService?.UpdateMultiClipboard(itemPaths, true);
        
        var itemNames = string.Join(", ", items.Select(item => item.Name));
        Log($"✂️ Cut {items.Count} item(s) to clipboard: {itemNames}");
    }

    private async void HandleDeleteWithConfirmation(BrowserItem item)
    {
        if (_contentDialogService == null || item == null)
        {
            Log("❌ Cannot delete item: Content dialog service not available");
            return;
        }

        var itemType = item.IsFolder ? "folder" : "file";
        var result = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = $"Delete {itemType}?",
                Content = $"Are you sure you want to delete '{item.Name}'?\n\nThis action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
            });

        if (result == ContentDialogResult.Primary)
        {
            Log($"🗑️ User confirmed deletion of: {item.Name}");
            HandleFileOperation(BrowserOperationContext.ForItem(item, PathTextBox.Text), FileOperationType.Delete);
        }
        else
        {
            Log($"🚫 User cancelled deletion of: {item.Name}");
        }
    }

    private async void HandleMultiDeleteWithConfirmation(List<BrowserItem> items)
    {
        if (_contentDialogService == null || !items.Any())
        {
            Log("❌ Cannot delete items: Content dialog service not available or no items selected");
            return;
        }

        var itemCount = items.Count;
        var itemNames = string.Join(", ", items.Take(3).Select(item => item.Name));
        var contentText = itemCount <= 3 
            ? $"Are you sure you want to delete the following {itemCount} item(s)?\n\n{itemNames}"
            : $"Are you sure you want to delete {itemCount} selected items?\n\n{itemNames}... and {itemCount - 3} more";

        contentText += "\n\nThis action cannot be undone.";

        var result = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = $"Delete {itemCount} items?",
                Content = contentText,
                PrimaryButtonText = "Delete All",
                CloseButtonText = "Cancel",
            });

        if (result == ContentDialogResult.Primary)
        {
            Log($"🗑️ User confirmed deletion of {itemCount} items");
            await HandleMultiDeleteOperation(items);
        }
        else
        {
            Log($"🚫 User cancelled deletion of {itemCount} items");
        }
    }


    private async Task HandleMultiDeleteOperation(List<BrowserItem> items)
    {
        if (_fileOperationService == null || !items.Any())
        {
            Log("❌ Multi-delete operation failed: File operation service not available or no items provided");
            return;
        }

        var itemPaths = items.Select(item => item.FullPath).ToList();
        var result = await _fileOperationService.DeleteMultiItemsAsync(itemPaths);
        
        if (!result.IsSuccess)
        {
            Log($"❌ Multi-delete operation failed: {result.ErrorMessage}");
        }
        else
        {
            Log($"✅ Successfully deleted {items.Count} item(s)");
            // Refresh directory to show changes
            await GoToFolder(PathTextBox.Text);
        }
    }

    private async Task<OperationResult> HandleFolderDownload(BrowserItem folderItem)
    {
        Log($"📁 Preparing to download directory: {folderItem.Name}");
        
        var dlg = new CommonOpenFileDialog
        {
            IsFolderPicker = true,
            Multiselect = false,
            Title = $"Select destination folder for {folderItem.Name}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
        {
            Log($"📂 Download destination selected: {dlg.FileName}");
            try
            {
                var localPath = Path.Combine(dlg.FileName, folderItem.Name);
                Directory.CreateDirectory(localPath);
                Log($"📁 Created local directory: {localPath}");

                await Task.Run(() => DownloadDirectory(folderItem.FullPath, localPath));

                Log($"✅ Successfully downloaded folder {folderItem.Name} to {localPath}");
                return OperationResult.Success();
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to download folder {folderItem.Name}: {ex.Message}");
                return OperationResult.Failure(ex.Message);
            }
        }
        else
        {
            Log($"🚫 Download cancelled by user");
            return OperationResult.Success();
        }
    }

    private async Task<OperationResult> HandleFileDownload(BrowserItem fileItem)
    {
        Log($"📄 Preparing to download file: {fileItem.Name} ({fileItem.SizeFormatted})");
        
        var saveDialog = new SaveFileDialog
        {
            FileName = fileItem.Name,
            Title = "Download File",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (saveDialog.ShowDialog() == true)
        {
            Log($"💾 Download destination selected: {saveDialog.FileName}");
            try
            {
                using var fs = new FileStream(saveDialog.FileName, FileMode.Create, FileAccess.Write);
                await Task.Run(() => _sftpClient!.DownloadFile(fileItem.FullPath, fs));
                
                Log($"✅ Successfully downloaded {fileItem.Name} to {saveDialog.FileName}");
                return OperationResult.Success();
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to download {fileItem.Name}: {ex.Message}");
                return OperationResult.Failure(ex.Message);
            }
        }
        else
        {
            Log($"🚫 Download cancelled by user");
            return OperationResult.Success();
        }
    }

    #region Connection and Navigation Methods

    private async void ControlLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized || _server == null) return;
        
        await ConnectToServer(_server);
        _isInitialized = true;
    }

    private async Task ConnectToServer(ServerViewModel server)
    {
        Log($"🔗 Attempting to connect to {server.Name} ({server.Host}:{server.Port})");
        ShowProgress(true, $"Connecting to {server.Name}...");

        try
        {
            _sftpClient = CreateSftpClient(server);
            await _sftpClient.ConnectAsync(CancellationToken.None);

            // Initialize file operation service
            _fileOperationService = new SftpFileOperationService(_sftpClient);
            SetupFileOperationServiceEvents();

            Log($"✅ Successfully connected to {server.Name} at {server.Host}:{server.Port}");
            Log($"📂 Working directory: {_sftpClient.WorkingDirectory}");
            
            await GoToFolder("~");
            TopToolbar.IsEnabled = true;
            Browser.UpdateEmptySpaceContextMenu(false);
            
            // Initialize paste button state
            PasteButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            Log($"❌ Connection failed to {server.Name}: {ex.Message}");
        }
        finally
        {
            ShowProgress(false);
        }
    }

    private SftpClient CreateSftpClient(ServerViewModel server)
    {
        ConnectionInfo connectionInfo;
        
        if (server.UseKeyFile)
        {
            Log($"🔑 Using key file authentication: {server.PrivateKeyFilePath}");
            var privateKey = new PrivateKeyFile(server.PrivateKeyFilePath, server.KeyFilePassword);
            var keyFiles = new[] { privateKey };
            connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, 
                new PrivateKeyAuthenticationMethod(server.Username, keyFiles));
        }
        else
        {
            Log($"🔐 Using password authentication for user: {server.Username}");
            connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, 
                new PasswordAuthenticationMethod(server.Username, server.Password));
        }

        return new SftpClient(connectionInfo);
    }

    private void SetupFileOperationServiceEvents()
    {
        if (_fileOperationService == null) return;
        
        _fileOperationService.LogRequested += Log;
        _fileOperationService.ProgressChanged += (show) => ShowProgress(show, show ? "Processing file operation..." : "");
        _fileOperationService.ClipboardStateChanged += OnClipboardStateChanged;
    }

    private void OnClipboardStateChanged()
    {
        var hasClipboardContent = _fileOperationService?.HasClipboardContent ?? false;
        
        // Update browser context menu
        Browser.UpdateEmptySpaceContextMenu(hasClipboardContent);
        
        // Update paste button state
        PasteButton.IsEnabled = hasClipboardContent;
        
        // Update tooltip based on clipboard state
        if (hasClipboardContent)
        {
            var clipboardPaths = _fileOperationService?.ClipboardPaths ?? new List<string>();
            var isCut = _fileOperationService?.IsClipboardCut ?? false;
            var isMultiple = _fileOperationService?.HasMultipleClipboardItems ?? false;
            
            if (isMultiple)
            {
                PasteButton.ToolTip = $"Paste {clipboardPaths.Count} item(s) ({(isCut ? "Move" : "Copy")}) - Ctrl+V";
            }
            else
            {
                var fileName = Path.GetFileName(clipboardPaths.FirstOrDefault() ?? "");
                PasteButton.ToolTip = $"Paste '{fileName}' ({(isCut ? "Move" : "Copy")}) - Ctrl+V";
            }
        }
        else
        {
            PasteButton.ToolTip = "Paste (Ctrl+V) - No items in clipboard";
        }
    }

    private async void BrowserEnterRequested(BrowserItem obj)
    {
        if (obj.IsFolder)
        {
            Log($"📁 Navigating to folder: {obj.FullPath}");
            await GoToFolder(obj.FullPath);
        }
        else
        {
            Log($"📄 Opening file: {obj.Name}");
        }
    }

    private async Task GoToFolder(string path)
    {
        Log($"🔄 Loading directory: {path}");
        ShowProgress(true, $"Loading directory: {path}");
        Browser.Clear();
        PathTextBox.Text = path;

        // Add parent folder entry
        Browser.AddItem(new BrowserItem
        {
            Name = "..",
            FullPath = $"{path}/..",
            IsFolder = true,
            LastUpdated = DateTime.Now,
            Size = 0
        });

        int itemCount = 0;
        await foreach (var item in FileDriveControlGetDirectory(path))
        {
            Browser.AddItem(item);
            itemCount++;
        }

        Log($"✅ Directory loaded successfully: {itemCount} items found in {path}");
        ShowProgress(false);
    }

    #endregion

    #region Existing Helper Methods (kept for compatibility)

    private async IAsyncEnumerable<BrowserItem> FileDriveControlGetDirectory(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        path = ResolveSftpPath(path);
        Log($"📋 Listing contents of: {path}");

        IAsyncEnumerable<Renci.SshNet.Sftp.ISftpFile> dirStream;

        try
        {
            dirStream = _sftpClient!.ListDirectoryAsync(path, cancellationToken);
        }
        catch (Exception ex)
        {
            Log($"❌ Failed to list directory {path}: {ex.Message}");
            yield break;
        }

        await foreach (var item in dirStream.WithCancellation(cancellationToken))
        {
            if (item.Name == "." || item.Name == "..") continue;

            yield return new BrowserItem
            {
                Name = item.Name,
                FullPath = item.FullName,
                LastUpdated = item.LastWriteTime,
                IsFolder = item.IsDirectory,
                Size = item.IsDirectory ? 0 : item.Attributes.Size,
            };
        }
    }

    private void DownloadDirectory(string remotePath, string localPath)
    {
        Log($"📂 Starting recursive download from {remotePath} to {localPath}");
        var files = _sftpClient!.ListDirectory(remotePath);

        foreach (var file in files)
        {
            if (file.Name == "." || file.Name == "..") continue;

            var localFilePath = Path.Combine(localPath, file.Name);
            var remoteFilePath = file.FullName;

            if (file.IsDirectory)
            {
                Log($"📁 Creating directory: {localFilePath}");
                Directory.CreateDirectory(localFilePath);
                DownloadDirectory(remoteFilePath, localFilePath);
            }
            else
            {
                Log($"📄 Downloading file: {file.Name} ({file.Attributes.Size:N0} bytes)");
                using var fs = new FileStream(localFilePath, FileMode.Create, FileAccess.Write);
                _sftpClient.DownloadFile(remoteFilePath, fs);
                Log($"✅ File downloaded: {file.Name}");
            }
        }
        Log($"📂 Completed recursive download for: {remotePath}");
    }

    private string ResolveSftpPath(string path)
    {
        if (string.IsNullOrEmpty(path) || _sftpClient?.WorkingDirectory == null)
            return path;
        return path.Replace("~", _sftpClient.WorkingDirectory);
    }

    #endregion

    #region UI Event Handlers

    private async void SftpUserControl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Handle keyboard shortcuts
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.V:
                    // Ctrl+V - Paste
                    if (PasteButton.IsEnabled)
                    {
                        Log($"📋 Paste shortcut (Ctrl+V) pressed for current directory: {PathTextBox.Text}");
                        HandleFileOperation(BrowserOperationContext.ForDirectory(PathTextBox.Text), FileOperationType.Paste);
                        e.Handled = true;
                    }
                    break;

                case System.Windows.Input.Key.C:
                    // Ctrl+C - Copy selected items
                    HandleMultiSelectOperation(FileOperationType.Copy);
                    e.Handled = true;
                    break;

                case System.Windows.Input.Key.X:
                    // Ctrl+X - Cut selected items
                    HandleMultiSelectOperation(FileOperationType.Cut);
                    e.Handled = true;
                    break;
            }
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        Log($"🔄 Refreshing current directory: {PathTextBox.Text}");
        await GoToFolder(PathTextBox.Text);
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var parentPath = $"{PathTextBox.Text}/..";
        Log($"⬅️ Navigating back to parent directory: {parentPath}");
        await GoToFolder(parentPath);
    }

    private async void PathTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            Log($"📍 Manual navigation to: {PathTextBox.Text}");
            await GoToFolder(PathTextBox.Text);
        }
    }

    private async void CreateFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Log($"📁 Create new folder requested in: {PathTextBox.Text}");
        Browser.StartNewFolderCreation();
    }

    private async void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        Log($"📋 Paste button clicked for current directory: {PathTextBox.Text}");
        HandleFileOperation(BrowserOperationContext.ForDirectory(PathTextBox.Text), FileOperationType.Paste);
    }

    private async void BrowserItemRenamed(BrowserItem item, string newName)
    {
        Log($"✏️ Inline rename: {item.OriginalName} → {newName}");
        var context = BrowserOperationContext.ForRename(item, PathTextBox.Text, newName);
        await PerformRename(context);
    }

    private async void BrowserNewFolderCreated(BrowserItem item)
    {
        Log($"📁 Creating new folder: {item.Name}");
        var context = BrowserOperationContext.ForNewFolder(PathTextBox.Text, item.Name);
        await PerformCreateFolder(context, item);
    }

    private void BrowserItemEditCancelled(BrowserItem item)
    {
        Log($"🚫 Edit cancelled for: {item.Name}");
    }

    private async Task PerformCreateFolder(BrowserOperationContext context, BrowserItem newFolderItem)
    {
        if (_fileOperationService == null) return;

        var result = await _fileOperationService.CreateFolderAsync(context.AdditionalData!, context.CurrentDirectory);
        
        if (result.IsSuccess)
        {
            newFolderItem.FullPath = result.Data as string ?? "";
            newFolderItem.CommitEdit();
        }
        else
        {
            Browser.RemoveItem(newFolderItem);
        }
    }

    private async Task PerformRename(BrowserOperationContext context)
    {
        if (_fileOperationService == null || context.TargetItem == null) return;

        var currentPath = context.TargetItem.FullPath;
        var parentPath = Path.GetDirectoryName(currentPath.Replace('/', Path.DirectorySeparatorChar))?.Replace(Path.DirectorySeparatorChar, '/');
        var newPath = $"{parentPath}/{context.AdditionalData}";

        var result = await _fileOperationService.RenameItemAsync(currentPath, newPath, context.TargetItem.IsFolder);
        
        if (result.IsSuccess)
        {
            context.TargetItem.FullPath = result.Data as string ?? newPath;
            context.TargetItem.CommitEdit();
        }
        else
        {
            context.TargetItem.Name = context.TargetItem.OriginalName;
            context.TargetItem.CommitEdit();
        }
    }


    #endregion

    #region Utility Methods

    private void ShowProgress(bool show)
    {
        TopToolbar.IsEnabled = !show;
        Progress.IsIndeterminate = show;
        Browser.IsBrowserEnabled = !show;
        ProgressOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowProgress(bool show, string message = "Operation in progress...")
    {
        TopToolbar.IsEnabled = !show;
        Progress.IsIndeterminate = show;
        Browser.IsBrowserEnabled = !show;
        ProgressOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ProgressText.Text = message;
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        Terminal.Text += $"[{timestamp}] {message}\n";
        
        if (Terminal.Parent is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToEnd();
        }
    }

    private void OnBrowserSelectionChanged(int selectedCount)
    {
        if (selectedCount > 0)
        {
            SelectionStatusText.Text = $"{selectedCount} item(s) selected";
            SelectionStatusPanel.Visibility = Visibility.Visible;
        }
        else
        {
            SelectionStatusPanel.Visibility = Visibility.Collapsed;
        }
    }

    #endregion

    #region Full Screen and Log Panel

    private void LogToggleButtonChecked(object sender, RoutedEventArgs e)
    {
        LogGrid.Visibility = Visibility.Visible;
        Log($"👁️ Operation log panel opened");
    }

    private void LogToggleButtonUnChecked(object sender, RoutedEventArgs e)
    {
        LogGrid.Visibility = Visibility.Collapsed;
        Log($"👁️ Operation log panel closed");
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        Log($"🖥️ Entering full screen mode");
        FullScreenButton.IsEnabled = false;
        BrowserContentControl.Content = null;
        var fullScreen = new FullScreenWindow(Browser, _server.Name);
        fullScreen.Show();
        fullScreen.Closed += (s, args) =>
        {
            Log($"🖥️ Exiting full screen mode");
            BrowserContentControl.Content = Browser;
            FullScreenButton.IsEnabled = true;
        };
    }

    #endregion

    public void Dispose()
    {
        if (_sftpClient is not null)
        {
            Log($"🔌 Disconnecting from server");
            _sftpClient.Disconnect();
            _sftpClient.Dispose();
            Log($"✅ Server connection closed");
        }
    }
}

public enum FileOperationType
{
    Cut,
    Copy,
    Paste,
    Upload,
    Download,
    NewFolder,
    Delete
}