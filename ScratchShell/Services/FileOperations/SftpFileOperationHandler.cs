using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using ScratchShell.Services;
using ScratchShell.Services.EventHandlers;
using ScratchShell.Services.Navigation;
using ScratchShell.UserControls.BrowserControl;
using ScratchShell.Views.Dialog;
using ScratchShell.Resources;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.Services.FileOperations
{
    /// <summary>
    /// Handles all SFTP file operations with proper error handling and user feedback
    /// </summary>
    public class SftpFileOperationHandler : ISftpFileOperationHandler
    {
        private readonly ISftpLogger _logger;
        private readonly IContentDialogService _contentDialogService;
        private readonly BrowserUserControl _browser;
        private ISftpFileOperationService? _fileOperationService;
        private ISftpNavigationManager? _navigationManager;
        private CancellationTokenSource? _currentOperationCancellation;

        public bool HasClipboardContent => _fileOperationService?.HasClipboardContent ?? false;

        public SftpFileOperationHandler(ISftpLogger logger, IContentDialogService contentDialogService, BrowserUserControl browser)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _contentDialogService = contentDialogService ?? throw new ArgumentNullException(nameof(contentDialogService));
            this._browser = browser;

            // Subscribe to browser cancel requests so user can abort long operations
            _browser.CancelRequested += OnBrowserCancelRequested;
        }

        public void Initialize(ISftpFileOperationService? fileOperationService, ISftpNavigationManager? navigationManager)
        {
            _fileOperationService = fileOperationService;
            _navigationManager = navigationManager;
            
            if (_fileOperationService != null)
            {
                _fileOperationService.LogRequested += _logger.LogInfo;
                _fileOperationService.ClipboardStateChanged += OnClipboardStateChanged;
            }
        }

        private void OnBrowserCancelRequested()
        {
            try
            {
                if (_currentOperationCancellation != null && !_currentOperationCancellation.IsCancellationRequested)
                {
                    _logger.LogInfo("Cancellation requested by user - attempting to cancel current operation");
                    _currentOperationCancellation.Cancel();
                }
                else
                {
                    _logger.LogDebug("Cancel requested but no active cancellable operation");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while processing cancellation request", ex);
            }
        }

        public async Task HandleCutAsync(BrowserItem item)
        {
            try
            {
                _fileOperationService?.UpdateClipboard(item.FullPath, true);
                _logger.LogInfo($"Cut item to clipboard: {item.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error cutting item {item.Name}", ex);
            }
        }

        public async Task HandleCopyAsync(BrowserItem item)
        {
            try
            {
                _fileOperationService?.UpdateClipboard(item.FullPath, false);
                _logger.LogInfo($"Copied item to clipboard: {item.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error copying item {item.Name}", ex);
            }
        }

        public async Task HandlePasteAsync(string currentDirectory)
        {
            if (_fileOperationService?.HasClipboardContent != true)
            {
                _logger.LogWarning(Langauge.FileOp_ClipboardEmpty);
                return;
            }
            await ExecuteWithCancellationAsync(async (cancellationToken) =>
            {
                var result = await _fileOperationService.PasteItemAsync(currentDirectory, cancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogError($"Paste operation failed: {result.ErrorMessage}");
                }
                else
                {
                    _logger.LogInfo("Paste operation completed successfully");
                }
                return result;
            });
        }

        public async Task HandleUploadAsync(string currentDirectory)
        {
            try
            {
                var openDialog = new OpenFileDialog
                {
                    Title = Langauge.FileOp_UploadFile,
                    Multiselect = false
                };

                if (openDialog.ShowDialog() == true)
                {
                    var localFilePath = openDialog.FileName;
                    var remotePath = $"{currentDirectory}/{Path.GetFileName(localFilePath)}";

                    await ExecuteWithCancellationAsync(async (cancellationToken) =>
                    {
                        return await _fileOperationService!.UploadFileAsync(localFilePath, remotePath, cancellationToken);
                    });
                }
                else
                {
                    _logger.LogInfo(Langauge.FileOp_UploadCancelledByUser);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during upload operation", ex);
                // Ensure directory refresh even if there's an error in the upload dialog or preparation
                await RefreshDirectoryAsync();
            }
        }

        public async Task HandleDownloadAsync(BrowserItem item)
        {
            try
            {
                _logger.LogInfo($"Download requested for: {item.Name}");

                if (item.IsFolder)
                {
                    await HandleFolderDownloadAsync(item);
                }
                else
                {
                    await HandleFileDownloadAsync(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Download operation failed for {item.Name}", ex);
                // Download operations don't modify the remote file system, so no refresh needed
            }
        }

        public async Task HandleDeleteAsync(BrowserItem item)
        {
            var itemType = item.IsFolder ? Langauge.General_Folder.ToLower() : Langauge.General_File.ToLower();
            var title = string.Format(Langauge.FileOp_DeleteConfirmTitle, itemType);
            var message = string.Format(Langauge.FileOp_DeleteConfirmMessage, item.Name);
            
            if (await ShowConfirmationDialog(title, message))
            {
                _logger.LogInfo($"User confirmed deletion of: {item.Name}");
                await ExecuteDeleteAsync(item);
            }
            else
            {
                _logger.LogInfo($"User cancelled deletion of: {item.Name}");
                // No need to refresh if user cancelled
            }
        }

        public async Task HandleCreateFolderAsync(string currentDirectory, string folderName)
        {
            await ExecuteWithCancellationAsync(async (cancellationToken) =>
            {
                return await _fileOperationService!.CreateFolderAsync(folderName, currentDirectory, cancellationToken);
            });
        }

        public async Task HandleRenameAsync(BrowserItem item, string newName)
        {
            try
            {
                var currentPath = item.FullPath;
                var parentPath = Path.GetDirectoryName(currentPath.Replace('/', Path.DirectorySeparatorChar))?.Replace(Path.DirectorySeparatorChar, '/');
                var newPath = $"{parentPath}/{newName}";

                await ExecuteWithCancellationAsync(async (cancellationToken) =>
                {
                    return await _fileOperationService!.RenameItemAsync(currentPath, newPath, item.IsFolder, cancellationToken);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error renaming item {item.Name}", ex);
            }
        }

        public async Task HandleMultiCopyAsync(List<BrowserItem> items)
        {
            try
            {
                var itemPaths = items.Select(item => item.FullPath).ToList();
                _fileOperationService?.UpdateMultiClipboard(itemPaths, false);

                var itemNames = string.Join(", ", items.Select(item => item.Name));
                _logger.LogInfo($"Copied {items.Count} item(s) to clipboard: {itemNames}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during multi-copy operation", ex);
            }
        }

        public async Task HandleMultiCutAsync(List<BrowserItem> items)
        {
            try
            {
                var itemPaths = items.Select(item => item.FullPath).ToList();
                _fileOperationService?.UpdateMultiClipboard(itemPaths, true);

                var itemNames = string.Join(", ", items.Select(item => item.Name));
                _logger.LogInfo($"Cut {items.Count} item(s) to clipboard: {itemNames}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during multi-cut operation", ex);
            }
        }

        public async Task HandleMultiDeleteAsync(List<BrowserItem> items)
        {
            var itemCount = items.Count;
            var itemNames = string.Join(", ", items.Take(3).Select(item => item.Name));
            var title = string.Format(Langauge.FileOp_DeleteMultiConfirmTitle, itemCount);
            string contentText;

            if (itemCount <= 3)
            {
                contentText = string.Format(Langauge.FileOp_DeleteMultiConfirmMessage, itemCount, itemNames);
            }
            else
            {
                contentText = string.Format(Langauge.FileOp_DeleteMultiConfirmMessageMany, itemCount, itemNames, itemCount - 3);
            }

            if (await ShowConfirmationDialog(title, contentText))
            {
                _logger.LogInfo($"User confirmed deletion of {itemCount} items");
                await ExecuteMultiDeleteAsync(items);
            }
            else
            {
                _logger.LogInfo($"User cancelled deletion of {itemCount} items");
                // No need to refresh if user cancelled
            }
        }

        public async Task HandleDragDropUploadAsync(string[] files, string currentDirectory)
        {
            if (files == null || files.Length == 0)
            {
                _logger.LogWarning(Langauge.FileOp_NoFilesForUpload);
                return;
            }

            var statusMessage = string.Format(Langauge.FileOp_DragDropUploadInitiated, files.Length);
            _logger.LogInfo(statusMessage);
            
            foreach (var file in files)
            {
                try
                {
                    var isDirectory = Directory.Exists(file);
                    var fileName = Path.GetFileName(file);
                    var itemType = isDirectory ? Langauge.General_Folder : Langauge.General_File;
                    _logger.LogInfo($"{itemType}: {fileName}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error checking file {file}", ex);
                }
            }

            await ExecuteWithCancellationAsync(async (cancellationToken) =>
            {
                return await _fileOperationService!.UploadMultipleFilesAsync(files, currentDirectory, cancellationToken);
            });
        }

        private async Task<bool> ShowConfirmationDialog(string title, string content)
        {
            try
            {
                // Create a simple message dialog since ShowSimpleDialogAsync doesn't exist
                var dialog = new MessageDialog(_contentDialogService, title, content);
                var result = await dialog.ShowAsync();
                return result == ContentDialogResult.Primary;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error showing confirmation dialog", ex);
                // Fallback to MessageBox for critical operations
                var result = System.Windows.MessageBox.Show(content, title, System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question);
                return result == System.Windows.MessageBoxResult.Yes;
            }
        }

        private async Task HandleFolderDownloadAsync(BrowserItem folderItem)
        {
            _logger.LogInfo($"Preparing to download directory: {folderItem.Name}");

            var title = string.Format(Langauge.FileOp_SelectDestinationFolder, folderItem.Name);
            var dlg = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Multiselect = false,
                Title = title,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {
                _logger.LogInfo($"Download destination selected: {dlg.FileName}");
                var localPath = Path.Combine(dlg.FileName, folderItem.Name);

                await ExecuteWithCancellationAsync(async (cancellationToken) =>
                {
                    return await _fileOperationService!.DownloadItemAsync(folderItem.FullPath, localPath, true, cancellationToken);
                });
            }
            else
            {
                _logger.LogInfo(Langauge.FileOp_DownloadCancelledByUser);
            }
        }

        private async Task HandleFileDownloadAsync(BrowserItem fileItem)
        {
            _logger.LogInfo($"Preparing to download file: {fileItem.Name} ({fileItem.SizeFormatted})");

            var saveDialog = new SaveFileDialog
            {
                FileName = fileItem.Name,
                Title = Langauge.FileOp_DownloadFile,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (saveDialog.ShowDialog() == true)
            {
                _logger.LogInfo($"Download destination selected: {saveDialog.FileName}");

                await ExecuteWithCancellationAsync(async (cancellationToken) =>
                {
                    return await _fileOperationService!.DownloadItemAsync(fileItem.FullPath, saveDialog.FileName, false, cancellationToken);
                });
            }
            else
            {
                _logger.LogInfo(Langauge.FileOp_DownloadCancelledByUser);
            }
        }

        private async Task ExecuteDeleteAsync(BrowserItem item)
        {
            await ExecuteWithCancellationAsync(async (cancellationToken) =>
            {
                return await _fileOperationService!.DeleteItemAsync(item.FullPath, cancellationToken);
            });
        }

        private async Task ExecuteMultiDeleteAsync(List<BrowserItem> items)
        {
            await ExecuteWithCancellationAsync(async (cancellationToken) =>
            {
                var itemPaths = items.Select(item => item.FullPath).ToList();
                return await _fileOperationService!.DeleteMultiItemsAsync(itemPaths, cancellationToken);
            });
        }

        /// <summary>
        /// Helper method to refresh the directory listing
        /// </summary>
        private async Task RefreshDirectoryAsync()
        {
            try
            {
                if (_navigationManager != null)
                {
                    _logger.LogDebug("Refreshing directory listing");
                    await _navigationManager.RefreshCurrentDirectoryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to refresh directory", ex);
            }
        }

        private async Task<OperationResult> ExecuteWithCancellationAsync(Func<CancellationToken, Task<OperationResult>> operation)
        {
            try
            {
                _currentOperationCancellation?.Dispose();
                _currentOperationCancellation = new CancellationTokenSource();
                var result = await operation(_currentOperationCancellation.Token);

                if (!result.IsSuccess && !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    if (result.ErrorMessage.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInfo("Operation was cancelled");
                    }
                    else
                    {
                        _logger.LogError($"Operation failed: {result.ErrorMessage}");
                    }
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Operation was cancelled by user");
                return OperationResult.Failure(Langauge.FileOp_OperationCancelled);
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error during operation", ex);
                return OperationResult.Failure(ex.Message);
            }
            finally
            {
                _currentOperationCancellation?.Dispose();
                _currentOperationCancellation = null;
                
                // Always refresh the directory listing after any file operation
                await RefreshDirectoryAsync();
            }
        }

        private void OnClipboardStateChanged()
        {
            _logger.LogDebug("Clipboard state changed");
            // This event can be used to update UI elements that depend on clipboard state
        }

        public void Dispose()
        {
            try
            {
                _browser.CancelRequested -= OnBrowserCancelRequested;
                _currentOperationCancellation?.Cancel();
                _currentOperationCancellation?.Dispose();
                _currentOperationCancellation = null;

                if (_fileOperationService != null)
                {
                    _fileOperationService.LogRequested -= _logger.LogInfo;
                    _fileOperationService.ClipboardStateChanged -= OnClipboardStateChanged;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error disposing file operation handler", ex);
            }
        }
    }
}