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
using System.Windows;
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
    private CancellationTokenSource? _currentOperationCancellation;
    private bool _isDirectoryLoading = false; // Track if GoToFolder is in progress

    // Navigation history for Back/Forward functionality
    private readonly List<string> _navigationHistory = new();

    private int _currentHistoryIndex = -1;
    private bool _isNavigatingFromHistory = false;

    public BrowserUserControl Browser { get; }

    public SftpUserControl(ServerViewModel server, IContentDialogService contentDialogService)
    {
        InitializeComponent();
        _server = server;
        _contentDialogService = contentDialogService;

        Browser = new BrowserUserControl();
        BrowserContentControl.Content = Browser;

        this.Loaded += ControlLoaded;
        // Don't disable entire TopToolbar to keep LogToggleButton always enabled
        // Individual buttons will be enabled/disabled as needed
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

        // Refresh requested
        Browser.RefreshRequested += async () => await SafeRefreshDirectory(PathTextBox.Text, "user refresh request");

        // Progress and cancel events - NEW
        Browser.ProgressChanged += OnBrowserProgressChanged;
        Browser.CancelRequested += OnBrowserCancelRequested;

        // Drag and drop events - NEW
        Browser.FilesDropped += OnBrowserFilesDropped;
    }

    private async void HandleFileOperation(BrowserOperationContext context, FileOperationType operationType)
    {
        if (_fileOperationService == null)
        {
            Log("❌ File operation service not available");
            return;
        }

        Log($"🔄 {operationType} operation requested for: {context.GetDisplayName()}");

        // Create cancellation token early for operations that need it
        var needsCancellationToken = operationType == FileOperationType.Paste ||
                                   operationType == FileOperationType.Upload ||
                                   operationType == FileOperationType.Download ||
                                   operationType == FileOperationType.Delete;

        var shouldRefreshOnSuccess = operationType == FileOperationType.Paste ||
                                   operationType == FileOperationType.Upload ||
                                   operationType == FileOperationType.NewFolder ||
                                   operationType == FileOperationType.Delete;

        if (needsCancellationToken)
        {
            // Create cancellation token before starting the operation
            _currentOperationCancellation?.Dispose();
            _currentOperationCancellation = new CancellationTokenSource();
            Log($"🔧 Created cancellation token for {operationType} operation");
        }

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
                
                // Check if the failure was due to cancellation
                if (result.ErrorMessage?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Log($"🔄 Operation was cancelled, refreshing directory");
                    if (shouldRefreshOnSuccess)
                    {
                        await SafeRefreshDirectory(PathTextBox.Text, "after cancelled operation");
                    }
                }
            }
            else if (shouldRefreshOnSuccess)
            {
                // Refresh directory for operations that modify the file system
                await SafeRefreshDirectory(PathTextBox.Text, "after successful operation");
            }
        }
        catch (OperationCanceledException)
        {
            Log($"🚫 {operationType} operation was cancelled");
            if (shouldRefreshOnSuccess)
            {
                await SafeRefreshDirectory(PathTextBox.Text, "after operation cancellation");
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Unexpected error during {operationType} operation: {ex.Message}");
        }
        finally
        {
            // Clean up cancellation token if we created one
            if (needsCancellationToken)
            {
                _currentOperationCancellation?.Dispose();
                _currentOperationCancellation = null;
                Log($"🔧 Cleaned up cancellation token for {operationType} operation");
            }
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

        var cancellationToken = _currentOperationCancellation?.Token ?? CancellationToken.None;
        return await _fileOperationService.PasteItemAsync(context.CurrentDirectory, cancellationToken);
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

            var cancellationToken = _currentOperationCancellation?.Token ?? CancellationToken.None;
            return await _fileOperationService!.UploadFileAsync(localFilePath, remotePath, cancellationToken);
        }

        Log("🚫 Upload cancelled by user");
        return OperationResult.Success(); // User cancellation is not an error
    }

    private async Task<OperationResult> HandleDownloadOperation(BrowserOperationContext context)
    {
        if (context.TargetItem == null) return OperationResult.Failure("No item selected");

        Log($"⬇️ Download requested for: {context.TargetItem.Name}");

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
        catch (Exception ex)
        {
            Log($"❌ Download operation failed: {ex.Message}");
            return OperationResult.Failure(ex.Message);
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
            var localPath = Path.Combine(dlg.FileName, folderItem.Name);

            var cancellationToken = _currentOperationCancellation?.Token ?? CancellationToken.None;
            return await _fileOperationService!.DownloadItemAsync(folderItem.FullPath, localPath, true, cancellationToken);
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

            var cancellationToken = _currentOperationCancellation?.Token ?? CancellationToken.None;
            return await _fileOperationService!.DownloadItemAsync(fileItem.FullPath, saveDialog.FileName, false, cancellationToken);
        }
        else
        {
            Log($"🚫 Download cancelled by user");
            return OperationResult.Success();
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

        Log($"🔍 HandleDeleteOperation called, checking cancellation token state");
        Log($"🔍 _currentOperationCancellation exists: {_currentOperationCancellation != null}");

        var cancellationToken = _currentOperationCancellation?.Token ?? CancellationToken.None;
        Log($"🔍 Using cancellation token - IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
        Log($"🔍 Token CanBeCanceled: {cancellationToken.CanBeCanceled}");

        return await _fileOperationService!.DeleteItemAsync(context.TargetItem.FullPath, cancellationToken);
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

        // Create cancellation token for multi-delete operation
        _currentOperationCancellation?.Dispose();
        _currentOperationCancellation = new CancellationTokenSource();
        Log($"🔧 Created cancellation token for multi-delete operation");

        try
        {
            var itemPaths = items.Select(item => item.FullPath).ToList();
            var cancellationToken = _currentOperationCancellation.Token;
            var result = await _fileOperationService.DeleteMultiItemsAsync(itemPaths, cancellationToken);

            if (!result.IsSuccess)
            {
                Log($"❌ Multi-delete operation failed: {result.ErrorMessage}");
                
                // Check if the failure was due to cancellation
                if (result.ErrorMessage?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Log($"🔄 Multi-delete was cancelled, refreshing directory");
                    await SafeRefreshDirectory(PathTextBox.Text, "after cancelled multi-delete");
                }
            }
            else
            {
                Log($"✅ Successfully deleted {items.Count} item(s)");
                // Refresh directory to show changes
                await SafeRefreshDirectory(PathTextBox.Text, "after successful multi-delete");
            }
        }
        catch (OperationCanceledException)
        {
            Log($"🚫 Multi-delete operation was cancelled by user");
            // Refresh directory even after cancellation
            await SafeRefreshDirectory(PathTextBox.Text, "after multi-delete cancellation");
        }
        catch (Exception ex)
        {
            Log($"❌ Unexpected error during multi-delete: {ex.Message}");
        }
        finally
        {
            // Clean up cancellation token
            _currentOperationCancellation?.Dispose();
            _currentOperationCancellation = null;
            Log($"🔧 Cleaned up cancellation token for multi-delete operation");
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
        Browser.ShowProgress(true, $"Connecting to {server.Name}...");

        try
        {
            _sftpClient = CreateSftpClient(server);
            await _sftpClient.ConnectAsync(CancellationToken.None);

            // Initialize file operation service
            _fileOperationService = new SftpFileOperationService(_sftpClient);
            SetupFileOperationServiceEvents();

            Log($"✅ Successfully connected to {server.Name} at {server.Host}:{server.Port}");
            Log($"📂 Working directory: {_sftpClient.WorkingDirectory}");

            // Initialize navigation history
            _navigationHistory.Clear();
            _currentHistoryIndex = -1;

            await GoToFolder("~");

            // Enable individual buttons that should be available after connection
            // Navigation button states will be set by UpdateNavigationButtonStates() in GoToFolder
            RefreshButton.IsEnabled = true;
            CreateFolderButton.IsEnabled = true;
            OptionsButton.IsEnabled = true;
            FullScreenButton.IsEnabled = true;
            PathTextBox.IsEnabled = true;
            // BackButton and ForwardButton states will be set by UpdateNavigationButtonStates
            // PasteButton will be enabled by OnClipboardStateChanged when clipboard has content
            // UpButton will be enabled/disabled based on path in UpdateNavigationButtonStates

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
            Browser.ShowProgress(false);
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
        _fileOperationService.ProgressChanged += (show, message, current, total) => Browser.ShowProgress(show, show ? message : "", current, total);
        _fileOperationService.ClipboardStateChanged += OnClipboardStateChanged;
    }

    private void OnClipboardStateChanged()
    {
        var hasClipboardContent = _fileOperationService?.HasClipboardContent ?? false;

        // Update browser context menu
        Browser.UpdateEmptySpaceContextMenu(hasClipboardContent);

        // Update paste button state - consider both clipboard content and current progress state
        // Only enable if there's clipboard content AND no operation is in progress (we'll track this through the button states)
        PasteButton.IsEnabled = hasClipboardContent && RefreshButton.IsEnabled; // RefreshButton is disabled during operations

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

    private void OnBrowserProgressChanged(bool show, string message, int? current, int? total)
    {
        // Update UI buttons based on progress state
        if (show)
        {
            // During operations, disable all buttons except LogToggleButton
            BackButton.IsEnabled = false;
            ForwardButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            UpButton.IsEnabled = false;
            CreateFolderButton.IsEnabled = false;
            PasteButton.IsEnabled = false;
            OptionsButton.IsEnabled = false;
            PathTextBox.IsEnabled = false;
        }
        else
        {
            // After operations, restore button states based on current path and other conditions
            var isAtRoot = IsAtRootPath();

            BackButton.IsEnabled = CanNavigateBack();
            ForwardButton.IsEnabled = CanNavigateForward();
            RefreshButton.IsEnabled = true;
            UpButton.IsEnabled = !isAtRoot;
            CreateFolderButton.IsEnabled = true;
            PasteButton.IsEnabled = _fileOperationService?.HasClipboardContent ?? false;
            OptionsButton.IsEnabled = true;
            FullScreenButton.IsEnabled = true;
            PathTextBox.IsEnabled = true;

            // Update tooltips
            UpdateNavigationButtonStates();
        }

        // Update main progress bar
        Progress.IsIndeterminate = show;
    }

    private void OnBrowserCancelRequested()
    {
        // Handle cancel request from browser control
        if (_currentOperationCancellation != null && !_currentOperationCancellation.Token.IsCancellationRequested)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Log($"🔍 [{timestamp}] Cancel requested from browser control");
            _currentOperationCancellation.Cancel();
            Log($"✅ [{timestamp}] Cancellation signal sent successfully");
            
            // Start a task to refresh after cancellation without blocking the UI
            _ = Task.Run(async () =>
            {
                // Wait a moment for the cancellation to be processed
                await Task.Delay(1000); // Give time for the operation to clean up
                
                // Execute refresh on UI thread, but only if not already loading
                Application.Current.Dispatcher.Invoke(async () =>
                {
                    try
                    {
                        if (!_isDirectoryLoading)
                        {
                            Log($"🔄 Refreshing directory after cancellation");
                            await SafeRefreshDirectory(PathTextBox.Text, "after cancellation");
                        }
                        else
                        {
                            Log($"⚠️ Directory refresh after cancellation skipped - loading already in progress");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"❌ Error refreshing after cancellation: {ex.Message}");
                    }
                });
            });
        }
        else
        {
            Log("❌ No cancellation token available or already cancelled");
        }
    }

    private async void OnBrowserFilesDropped(string[] files)
    {
        if (_fileOperationService == null)
        {
            Log("❌ File operation service not available");
            return;
        }

        if (files == null || files.Length == 0)
        {
            Log("❌ No files provided for upload");
            return;
        }

        Log($"🎯 Drag and drop upload initiated for {files.Length} item(s)");
        foreach (var file in files)
        {
            var isDirectory = Directory.Exists(file);
            var fileName = Path.GetFileName(file);
            Log($"📦 {(isDirectory ? "Folder" : "File")}: {fileName}");
        }

        // Create cancellation token for drag and drop upload operation
        _currentOperationCancellation?.Dispose();
        _currentOperationCancellation = new CancellationTokenSource();
        Log($"🔧 Created cancellation token for drag and drop upload operation");

        try
        {
            var cancellationToken = _currentOperationCancellation.Token;
            var result = await _fileOperationService.UploadMultipleFilesAsync(files, PathTextBox.Text, cancellationToken);

            if (!result.IsSuccess)
            {
                Log($"❌ Drag and drop upload failed: {result.ErrorMessage}");
                
                // Check if the failure was due to cancellation
                if (result.ErrorMessage?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Log($"🔄 Drag and drop upload was cancelled, refreshing directory");
                    await SafeRefreshDirectory(PathTextBox.Text, "after cancelled drag and drop");
                }
            }
            else
            {
                Log($"✅ Successfully completed drag and drop upload");
                // Refresh directory to show uploaded files
                await SafeRefreshDirectory(PathTextBox.Text, "after successful drag and drop");
            }
        }
        catch (OperationCanceledException)
        {
            Log($"🚫 Drag and drop upload was cancelled by user");
            // Refresh directory even after cancellation
            await SafeRefreshDirectory(PathTextBox.Text, "after drag and drop cancellation");
        }
        catch (Exception ex)
        {
            Log($"❌ Unexpected error during drag and drop upload: {ex.Message}");
        }
        finally
        {
            // Clean up cancellation token
            _currentOperationCancellation?.Dispose();
            _currentOperationCancellation = null;
            Log($"🔧 Cleaned up cancellation token for drag and drop upload operation");
        }
    }

    private async void BrowserEnterRequested(BrowserItem obj)
    {
        if (obj.IsFolder)
        {
            if (obj.Name == "..")
            {
                // Handle parent directory navigation
                var currentPath = PathTextBox.Text;
                var parentPath = GetParentPath(currentPath);
                Log($"📁 Navigating to parent directory: {parentPath}");
                await GoToFolder(parentPath);
            }
            else
            {
                Log($"📁 Navigating to folder: {obj.FullPath}");
                await GoToFolder(obj.FullPath);
            }
        }
        else
        {
            Log($"📄 Opening file: {obj.Name}");
        }
    }

    private async Task GoToFolder(string path)
    {
        // Prevent concurrent directory loading operations
        if (_isDirectoryLoading)
        {
            Log($"⚠️ Directory loading already in progress, ignoring request for: {path}");
            return;
        }

        try
        {
            _isDirectoryLoading = true;
            
            // Resolve path properly - handle cases like "/home/user/.."
            var resolvedPath = ResolvePath(path);

            Log($"🔄 Loading directory: {resolvedPath}");
            Browser.ShowProgress(true, $"Loading directory: {resolvedPath}");
            Browser.Clear();

            // Use the resolved path for the PathTextBox
            PathTextBox.Text = resolvedPath;

            // Add to navigation history before loading
            AddToNavigationHistory(resolvedPath);

            // Add parent folder entry
            Browser.AddItem(new BrowserItem
            {
                Name = "..",
                FullPath = $"{resolvedPath}/..",
                IsFolder = true,
                LastUpdated = DateTime.Now,
                Size = 0
            });

            int itemCount = 0;
            await foreach (var item in FileDriveControlGetDirectory(resolvedPath))
            {
                Browser.AddItem(item);
                itemCount++;
            }

            Log($"✅ Directory loaded successfully: {itemCount} items found in {resolvedPath}");
        }
        catch (Exception ex)
        {
            Log($"❌ Error loading directory {path}: {ex.Message}");
            throw; // Re-throw to maintain error handling behavior
        }
        finally
        {
            _isDirectoryLoading = false;
            Browser.ShowProgress(false);

            // Update navigation button states based on current path and history
            UpdateNavigationButtonStates();
        }
    }

    private bool IsAtRootPath()
    {
        var currentPath = PathTextBox.Text?.Trim();
        return string.IsNullOrEmpty(currentPath) || currentPath == "/" || currentPath == "~";
    }

    private void UpdateNavigationButtonStates()
    {
        var isAtRoot = IsAtRootPath();

        // Update Back button based on navigation history
        var canGoBack = CanNavigateBack();
        BackButton.IsEnabled = canGoBack;

        // Update Forward button based on navigation history
        var canGoForward = CanNavigateForward();
        ForwardButton.IsEnabled = canGoForward;

        // Up button is based on path, not history
        UpButton.IsEnabled = !isAtRoot;

        // Update tooltips to reflect the state
        if (canGoBack)
        {
            var previousPath = _navigationHistory[_currentHistoryIndex - 1];
            var previousFolderName = Path.GetFileName(previousPath) ?? previousPath;
            if (string.IsNullOrEmpty(previousFolderName) || previousFolderName == "/")
                previousFolderName = "Root";
            BackButton.ToolTip = $"Back to '{previousFolderName}' (Alt+Left Arrow)";
        }
        else
        {
            BackButton.ToolTip = "Back (disabled - no previous locations)";
        }

        if (canGoForward)
        {
            var nextPath = _navigationHistory[_currentHistoryIndex + 1];
            var nextFolderName = Path.GetFileName(nextPath) ?? nextPath;
            if (string.IsNullOrEmpty(nextFolderName) || nextFolderName == "/")
                nextFolderName = "Root";
            ForwardButton.ToolTip = $"Forward to '{nextFolderName}' (Alt+Right Arrow)";
        }
        else
        {
            ForwardButton.ToolTip = "Forward (disabled - no next locations)";
        }

        if (isAtRoot)
        {
            UpButton.ToolTip = "Up one level (disabled - at root)";
        }
        else
        {
            UpButton.ToolTip = "Up one level";
        }
    }

    private void UpdateNavigationHistory(string newPath)
    {
        // Remove any forward history if we're adding a new entry
        if (_currentHistoryIndex < _navigationHistory.Count - 1)
        {
            _navigationHistory.RemoveRange(_currentHistoryIndex + 1, _navigationHistory.Count - _currentHistoryIndex - 1);
        }

        // Add new entry to history
        _navigationHistory.Add(newPath);
        _currentHistoryIndex = _navigationHistory.Count - 1;

        Log($"📚 Navigation history updated: {_navigationHistory.Count} item(s)");
    }

    private void NavigateToPreviousFolder()
    {
        if (_currentHistoryIndex > 0)
        {
            _currentHistoryIndex--;
            var previousPath = _navigationHistory[_currentHistoryIndex];
            Log($"⬅️ Navigating back to: {previousPath}");
            _ = GoToFolder(previousPath);
        }
        else
        {
            Log("❌ No more history to navigate back");
        }
    }

    private void NavigateToNextFolder()
    {
        if (_currentHistoryIndex < _navigationHistory.Count - 1)
        {
            _currentHistoryIndex++;
            var nextPath = _navigationHistory[_currentHistoryIndex];
            Log($"➡️ Navigating forward to: {nextPath}");
            _ = GoToFolder(nextPath);
        }
        else
        {
            Log("❌ No more history to navigate forward");
        }
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        // Handle special cases
        if (path == "~")
        {
            return _sftpClient?.WorkingDirectory ?? "/";
        }

        // If path contains ".." resolve it
        if (path.Contains(".."))
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var resolvedParts = new List<string>();

            foreach (var part in parts)
            {
                if (part == "..")
                {
                    // Go up one level - remove the last directory if exists
                    if (resolvedParts.Count > 0)
                    {
                        resolvedParts.RemoveAt(resolvedParts.Count - 1);
                    }
                }
                else if (part != ".")
                {
                    resolvedParts.Add(part);
                }
            }

            // Construct the resolved path
            if (resolvedParts.Count == 0)
            {
                return "/";
            }
            else
            {
                return "/" + string.Join("/", resolvedParts);
            }
        }

        // Ensure path starts with /
        if (!path.StartsWith("/"))
        {
            path = "/" + path;
        }

        return path;
    }

    #endregion Connection and Navigation Methods

    #region Navigation History Management

    private void AddToNavigationHistory(string path)
    {
        // Don't add to history if we're navigating from history
        if (_isNavigatingFromHistory)
        {
            _isNavigatingFromHistory = false;
            return;
        }

        // Don't add duplicate consecutive entries
        if (_navigationHistory.Count > 0 && _navigationHistory[_currentHistoryIndex] == path)
        {
            return;
        }

        // If we're not at the end of history, remove everything after current position
        if (_currentHistoryIndex < _navigationHistory.Count - 1)
        {
            _navigationHistory.RemoveRange(_currentHistoryIndex + 1, _navigationHistory.Count - _currentHistoryIndex - 1);
        }

        // Add new path to history
        _navigationHistory.Add(path);
        _currentHistoryIndex = _navigationHistory.Count - 1;

        // Keep history size reasonable (max 50 entries)
        if (_navigationHistory.Count > 50)
        {
            _navigationHistory.RemoveAt(0);
            _currentHistoryIndex--;
        }

        UpdateNavigationButtonStates();
        Log($"📚 Added to navigation history: {path} (Index: {_currentHistoryIndex}, Total: {_navigationHistory.Count})");
    }

    private async Task NavigateBack()
    {
        if (_currentHistoryIndex <= 0)
        {
            Log("❌ Cannot navigate back: No previous locations in history");
            return;
        }

        _currentHistoryIndex--;
        var previousPath = _navigationHistory[_currentHistoryIndex];

        Log($"⬅️ Navigating back to: {previousPath} (Index: {_currentHistoryIndex})");

        _isNavigatingFromHistory = true;
        await GoToFolder(previousPath);
    }

    private async Task NavigateForward()
    {
        if (_currentHistoryIndex >= _navigationHistory.Count - 1)
        {
            Log("❌ Cannot navigate forward: No next locations in history");
            return;
        }

        _currentHistoryIndex++;
        var nextPath = _navigationHistory[_currentHistoryIndex];

        Log($"➡️ Navigating forward to: {nextPath} (Index: {_currentHistoryIndex})");

        _isNavigatingFromHistory = true;
        await GoToFolder(nextPath);
    }

    private bool CanNavigateBack()
    {
        return _currentHistoryIndex > 0;
    }

    private bool CanNavigateForward()
    {
        return _currentHistoryIndex < _navigationHistory.Count - 1;
    }

    #endregion Navigation History Management

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

    /// <summary>
    /// Safely requests a directory refresh, respecting the current loading state
    /// </summary>
    /// <param name="path">The path to load</param>
    /// <param name="reason">The reason for the refresh (for logging)</param>
    /// <returns>True if refresh was attempted, false if skipped due to loading in progress</returns>
    private async Task<bool> SafeRefreshDirectory(string path, string reason = "refresh")
    {
        if (_isDirectoryLoading)
        {
            Log($"⚠️ Directory {reason} skipped - loading already in progress for: {path}");
            return false;
        }

        try
        {
            Log($"🔄 Directory {reason} requested for: {path}");
            await GoToFolder(path);
            return true;
        }
        catch (Exception ex)
        {
            Log($"❌ Error during directory {reason}: {ex.Message}");
            return false;
        }
    }

    #endregion Existing Helper Methods (kept for compatibility)

    #region UI Event Handlers

    private async void SftpUserControl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Handle navigation shortcuts
        if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Left:
                    // Alt+Left Arrow - Navigate Back
                    if (BackButton.IsEnabled)
                    {
                        await NavigateBack();
                        e.Handled = true;
                    }
                    break;

                case System.Windows.Input.Key.Right:
                    // Alt+Right Arrow - Navigate Forward
                    if (ForwardButton.IsEnabled)
                    {
                        await NavigateForward();
                        e.Handled = true;
                    }
                    break;
            }
        }
        // Handle other keyboard shortcuts
        else if (Keyboard.Modifiers == ModifierKeys.Control)
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
        await SafeRefreshDirectory(PathTextBox.Text, "refresh button click");
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateBack();
    }

    private async void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateForward();
    }

    private async void UpButton_Click(object sender, RoutedEventArgs e)
    {
        var currentPath = PathTextBox.Text;
        var parentPath = GetParentPath(currentPath);

        Log($"⬆️ Going up one level to: {parentPath}");
        await GoToFolder(parentPath);
    }

    private string GetParentPath(string currentPath)
    {
        // Handle special cases
        if (string.IsNullOrEmpty(currentPath) || currentPath == "/" || currentPath == "~")
        {
            return "/"; // Already at root
        }

        // Remove trailing slash if present
        currentPath = currentPath.TrimEnd('/');

        // If the path is just a single directory name, go to root
        if (!currentPath.Contains('/'))
        {
            return "/";
        }

        // Get the parent directory by removing the last segment
        var lastSlashIndex = currentPath.LastIndexOf('/');
        if (lastSlashIndex == 0)
        {
            // Parent is root directory
            return "/";
        }
        else if (lastSlashIndex > 0)
        {
            // Return the path up to the last slash
            return currentPath.Substring(0, lastSlashIndex);
        }

        // Fallback to root
        return "/";
    }

    private async void PathTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            var targetPath = PathTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(targetPath))
            {
                Log($"📍 Manual navigation to: {targetPath}");
                await GoToFolder(targetPath);
            }
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

        // Create cancellation token for create folder operation
        _currentOperationCancellation?.Dispose();
        _currentOperationCancellation = new CancellationTokenSource();
        Log($"🔧 Created cancellation token for create folder operation");

        try
        {
            var cancellationToken = _currentOperationCancellation.Token;
            var result = await _fileOperationService.CreateFolderAsync(context.AdditionalData!, context.CurrentDirectory, cancellationToken);

            if (result.IsSuccess)
            {
                newFolderItem.FullPath = result.Data as string ?? "";
                newFolderItem.CommitEdit();
            }
            else
            {
                Browser.RemoveItem(newFolderItem);
                
                // Check if the failure was due to cancellation and refresh
                if (result.ErrorMessage?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Log($"🔄 Folder creation was cancelled, refreshing directory");
                    await SafeRefreshDirectory(PathTextBox.Text, "after cancelled folder creation");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log($"🚫 Create folder operation was cancelled by user");
            Browser.RemoveItem(newFolderItem);
            // Refresh directory after cancellation
            await SafeRefreshDirectory(PathTextBox.Text, "after folder creation cancellation");
        }
        catch (Exception ex)
        {
            Log($"❌ Unexpected error during folder creation: {ex.Message}");
            Browser.RemoveItem(newFolderItem);
        }
        finally
        {
            // Clean up cancellation token
            _currentOperationCancellation?.Dispose();
            _currentOperationCancellation = null;
            Log($"🔧 Cleaned up cancellation token for create folder operation");
        }
    }

    private async Task PerformRename(BrowserOperationContext context)
    {
        if (_fileOperationService == null || context.TargetItem == null) return;

        // Create cancellation token for rename operation
        _currentOperationCancellation?.Dispose();
        _currentOperationCancellation = new CancellationTokenSource();
        Log($"🔧 Created cancellation token for rename operation");

        try
        {
            var currentPath = context.TargetItem.FullPath;
            var parentPath = Path.GetDirectoryName(currentPath.Replace('/', Path.DirectorySeparatorChar))?.Replace(Path.DirectorySeparatorChar, '/');
            var newPath = $"{parentPath}/{context.AdditionalData}";

            var cancellationToken = _currentOperationCancellation.Token;
            var result = await _fileOperationService.RenameItemAsync(currentPath, newPath, context.TargetItem.IsFolder, cancellationToken);

            if (result.IsSuccess)
            {
                context.TargetItem.FullPath = result.Data as string ?? newPath;
                context.TargetItem.CommitEdit();
            }
            else
            {
                context.TargetItem.Name = context.TargetItem.OriginalName;
                context.TargetItem.CommitEdit();
                
                // Check if the failure was due to cancellation and refresh
                if (result.ErrorMessage?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Log($"🔄 Rename operation was cancelled, refreshing directory");
                    await SafeRefreshDirectory(PathTextBox.Text, "after cancelled rename");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log($"🚫 Rename operation was cancelled by user");
            context.TargetItem.Name = context.TargetItem.OriginalName;
            context.TargetItem.CommitEdit();
            // Refresh directory after cancellation
            await SafeRefreshDirectory(PathTextBox.Text, "after rename cancellation");
        }
        catch (Exception ex)
        {
            Log($"❌ Unexpected error during rename: {ex.Message}");
            context.TargetItem.Name = context.TargetItem.OriginalName;
            context.TargetItem.CommitEdit();
        }
        finally
        {
            // Clean up cancellation token
            _currentOperationCancellation?.Dispose();
            _currentOperationCancellation = null;
            Log($"🔧 Cleaned up cancellation token for rename operation");
        }
    }

    #endregion UI Event Handlers

    #region Utility Methods

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

    #endregion Utility Methods

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
        var fullScreen = new FullScreenWindow(_contentDialogService, Browser, _server.Name);
        fullScreen.Show();
        fullScreen.Closed += (s, args) =>
        {
            Log($"🖥️ Exiting full screen mode");
            BrowserContentControl.Content = Browser;
            FullScreenButton.IsEnabled = true;
        };
    }

    #endregion Full Screen and Log Panel

    public void Dispose()
    {
        // Cancel any ongoing operations
        _currentOperationCancellation?.Cancel();
        _currentOperationCancellation?.Dispose();

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