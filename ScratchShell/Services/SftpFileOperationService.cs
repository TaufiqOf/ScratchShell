using Renci.SshNet;
using System.IO;

namespace ScratchShell.Services;

/// <summary>
/// Service for handling SFTP file operations with proper error handling and logging
/// </summary>
public interface ISftpFileOperationService
{
    Task<OperationResult> CreateFolderAsync(string folderName, string currentPath);
    Task<OperationResult> RenameItemAsync(string oldPath, string newPath, bool isFolder);
    Task<OperationResult> UploadFileAsync(string localFilePath, string remotePath);
    Task<OperationResult> PasteItemAsync(string destinationPath);
    Task<OperationResult> PasteMultiItemsAsync(string destinationPath);
    Task<OperationResult> DownloadItemAsync(string remotePath, string localPath, bool isFolder);
    Task<OperationResult> DeleteItemAsync(string itemPath);
    Task<OperationResult> DeleteMultiItemsAsync(List<string> itemPaths);
    void UpdateClipboard(string itemPath, bool isCut);
    void UpdateMultiClipboard(List<string> itemPaths, bool isCut);
    bool HasClipboardContent { get; }
    bool HasMultipleClipboardItems { get; }
    string? ClipboardPath { get; }
    List<string> ClipboardPaths { get; }
    bool IsClipboardCut { get; }
    event Action<string>? LogRequested;
    event Action<bool>? ProgressChanged;
    event Action? ClipboardStateChanged;
}

public class SftpFileOperationService : ISftpFileOperationService
{
    private readonly SftpClient _sftpClient;
    private string? _clipboardPath;
    private List<string> _clipboardPaths = new();
    private bool _clipboardIsCut;

    public bool HasClipboardContent => !string.IsNullOrEmpty(_clipboardPath) || _clipboardPaths.Any();
    public bool HasMultipleClipboardItems => _clipboardPaths.Count > 1;
    public string? ClipboardPath => _clipboardPath;
    public List<string> ClipboardPaths => _clipboardPaths.ToList();
    public bool IsClipboardCut => _clipboardIsCut;

    public event Action<string>? LogRequested;
    public event Action<bool>? ProgressChanged;
    public event Action? ClipboardStateChanged;

    public SftpFileOperationService(SftpClient sftpClient)
    {
        _sftpClient = sftpClient ?? throw new ArgumentNullException(nameof(sftpClient));
    }

    public async Task<OperationResult> CreateFolderAsync(string folderName, string currentPath)
    {
        try
        {
            ProgressChanged?.Invoke(true);
            var newFolderPath = $"{currentPath}/{folderName}";
            newFolderPath = ResolveSftpPath(newFolderPath);

            LogRequested?.Invoke($"?? Creating new folder '{folderName}' in current directory");
            LogRequested?.Invoke($"?? Full path: {newFolderPath}");

            await Task.Run(() => _sftpClient.CreateDirectory(newFolderPath));

            LogRequested?.Invoke($"? Successfully created folder '{folderName}'");
            return OperationResult.Success(newFolderPath);
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"? Failed to create folder '{folderName}': {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false);
        }
    }

    public async Task<OperationResult> RenameItemAsync(string oldPath, string newPath, bool isFolder)
    {
        try
        {
            ProgressChanged?.Invoke(true);
            var resolvedOldPath = ResolveSftpPath(oldPath);

            LogRequested?.Invoke($"?? Renaming {(isFolder ? "folder" : "file")}");
            LogRequested?.Invoke($"?? Old path: {resolvedOldPath}");
            LogRequested?.Invoke($"?? New path: {newPath}");

            await Task.Run(() => _sftpClient.RenameFile(resolvedOldPath, newPath));

            LogRequested?.Invoke($"? Successfully renamed item");
            return OperationResult.Success(newPath);
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"? Failed to rename item: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false);
        }
    }

    public async Task<OperationResult> UploadFileAsync(string localFilePath, string remotePath)
    {
        try
        {
            ProgressChanged?.Invoke(true);
            remotePath = ResolveSftpPath(remotePath);
            var fileInfo = new FileInfo(localFilePath);

            LogRequested?.Invoke($"?? Selected file for upload: {localFilePath} ({fileInfo.Length:N0} bytes)");
            LogRequested?.Invoke($"?? Upload destination: {remotePath}");

            using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);
            await Task.Run(() => _sftpClient.UploadFile(fs, remotePath));

            LogRequested?.Invoke($"? Successfully uploaded {Path.GetFileName(localFilePath)} to {remotePath}");
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"? Failed to upload {localFilePath}: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false);
        }
    }

    public async Task<OperationResult> PasteItemAsync(string destinationPath)
    {
        // If we have multiple items, use the multi-item paste method
        if (_clipboardPaths.Count > 1)
        {
            return await PasteMultiItemsAsync(destinationPath);
        }

        try
        {
            ProgressChanged?.Invoke(true);

            if (string.IsNullOrEmpty(_clipboardPath))
            {
                LogRequested?.Invoke($"? Paste operation failed: Clipboard is empty");
                return OperationResult.Failure("Clipboard is empty");
            }

            var fileName = Path.GetFileName(_clipboardPath);
            var fullDestinationPath = $"{destinationPath}/{fileName}";
            var resolvedSourcePath = ResolveSftpPath(_clipboardPath);
            var resolvedDestinationPath = ResolveSftpPath(fullDestinationPath);

            LogRequested?.Invoke(_clipboardIsCut ?
                $"?? Moving file from {resolvedSourcePath} to {resolvedDestinationPath}" :
                $"?? Copying file from {resolvedSourcePath} to {resolvedDestinationPath}");

            // Check if source is a directory
            var sourceFile = await Task.Run(() => _sftpClient.Get(resolvedSourcePath));

            if (sourceFile.IsDirectory)
            {
                await CopyDirectoryRecursive(resolvedSourcePath, resolvedDestinationPath);
            }
            else
            {
                LogRequested?.Invoke($"?? Downloading file to memory for transfer");
                using var ms = new MemoryStream();
                await Task.Run(() => _sftpClient.DownloadFile(resolvedSourcePath, ms));
                ms.Position = 0;

                LogRequested?.Invoke($"?? Uploading file to destination");
                await Task.Run(() => _sftpClient.UploadFile(ms, resolvedDestinationPath));
            }

            if (_clipboardIsCut)
            {
                LogRequested?.Invoke($"??? Deleting original: {resolvedSourcePath}");
                if (sourceFile.IsDirectory)
                {
                    await DeleteDirectoryRecursive(resolvedSourcePath);
                }
                else
                {
                    await Task.Run(() => _sftpClient.DeleteFile(resolvedSourcePath));
                }
                LogRequested?.Invoke($"? Successfully moved {resolvedSourcePath} to {resolvedDestinationPath}");
            }
            else
            {
                LogRequested?.Invoke($"? Successfully copied {resolvedSourcePath} to {resolvedDestinationPath}");
            }

            // Clear clipboard after operation
            _clipboardPath = null;
            _clipboardPaths.Clear();
            _clipboardIsCut = false;
            ClipboardStateChanged?.Invoke();

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"? Paste operation failed: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false);
        }
    }

    public void UpdateClipboard(string itemPath, bool isCut)
    {
        _clipboardPath = itemPath;
        _clipboardPaths.Clear();
        _clipboardPaths.Add(itemPath);
        _clipboardIsCut = isCut;
        ClipboardStateChanged?.Invoke();

        LogRequested?.Invoke(isCut ?
            $"?? Cut to clipboard: {itemPath}" :
            $"?? Copied to clipboard: {itemPath}");
    }

    public void UpdateMultiClipboard(List<string> itemPaths, bool isCut)
    {
        _clipboardPaths.Clear();
        _clipboardPaths.AddRange(itemPaths);
        _clipboardPath = itemPaths.FirstOrDefault(); // For backward compatibility
        _clipboardIsCut = isCut;
        ClipboardStateChanged?.Invoke();

        var operation = isCut ? "Cut" : "Copied";
        var itemCount = itemPaths.Count;
        LogRequested?.Invoke($"{(isCut ? "??" : "??")} {operation} {itemCount} item(s) to clipboard");
    }

    public async Task<OperationResult> PasteMultiItemsAsync(string destinationPath)
    {
        try
        {
            ProgressChanged?.Invoke(true);

            if (!_clipboardPaths.Any())
            {
                LogRequested?.Invoke($"? Paste operation failed: Clipboard is empty");
                return OperationResult.Failure("Clipboard is empty");
            }

            var successCount = 0;
            var failCount = 0;
            var errors = new List<string>();

            LogRequested?.Invoke($"?? Starting paste operation for {_clipboardPaths.Count} item(s)");

            foreach (var sourcePath in _clipboardPaths)
            {
                try
                {
                    var fileName = Path.GetFileName(sourcePath);
                    var fullDestinationPath = $"{destinationPath}/{fileName}";
                    var resolvedSourcePath = ResolveSftpPath(sourcePath);
                    var resolvedDestinationPath = ResolveSftpPath(fullDestinationPath);

                    LogRequested?.Invoke(_clipboardIsCut ?
                        $"?? Moving {fileName}" :
                        $"?? Copying {fileName}");

                    // Check if source is a directory
                    var sourceFile = await Task.Run(() => _sftpClient.Get(resolvedSourcePath));

                    if (sourceFile.IsDirectory)
                    {
                        await CopyDirectoryRecursive(resolvedSourcePath, resolvedDestinationPath);
                    }
                    else
                    {
                        using var ms = new MemoryStream();
                        await Task.Run(() => _sftpClient.DownloadFile(resolvedSourcePath, ms));
                        ms.Position = 0;
                        await Task.Run(() => _sftpClient.UploadFile(ms, resolvedDestinationPath));
                    }

                    if (_clipboardIsCut)
                    {
                        if (sourceFile.IsDirectory)
                        {
                            await DeleteDirectoryRecursive(resolvedSourcePath);
                        }
                        else
                        {
                            await Task.Run(() => _sftpClient.DeleteFile(resolvedSourcePath));
                        }
                    }

                    successCount++;
                    LogRequested?.Invoke($"? Successfully {(_clipboardIsCut ? "moved" : "copied")} {fileName}");
                }
                catch (Exception ex)
                {
                    failCount++;
                    var fileName = Path.GetFileName(sourcePath);
                    var errorMsg = $"Failed to {(_clipboardIsCut ? "move" : "copy")} {fileName}: {ex.Message}";
                    errors.Add(errorMsg);
                    LogRequested?.Invoke($"? {errorMsg}");
                }
            }

            // Clear clipboard after operation if all succeeded or if it was a cut operation
            if (failCount == 0 || _clipboardIsCut)
            {
                _clipboardPaths.Clear();
                _clipboardPath = null;
                _clipboardIsCut = false;
                ClipboardStateChanged?.Invoke();
            }

            var summary = $"Operation completed: {successCount} successful, {failCount} failed";
            LogRequested?.Invoke($"?? {summary}");

            if (failCount > 0)
            {
                return OperationResult.Failure($"{summary}. Errors: {string.Join("; ", errors)}");
            }

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"? Multi-paste operation failed: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false);
        }
    }

    private async Task CopyDirectoryRecursive(string sourcePath, string destinationPath)
    {
        // Create destination directory
        _sftpClient.CreateDirectory(destinationPath);

        // List all items in source directory
        var items = _sftpClient.ListDirectory(sourcePath);

        foreach (var item in items)
        {
            if (item.Name == "." || item.Name == "..") continue;

            var sourceItemPath = $"{sourcePath}/{item.Name}";
            var destItemPath = $"{destinationPath}/{item.Name}";

            if (item.IsDirectory)
            {
                await CopyDirectoryRecursive(sourceItemPath, destItemPath);
            }
            else
            {
                using var ms = new MemoryStream();
                await Task.Run(() => _sftpClient.DownloadFile(sourceItemPath, ms));
                ms.Position = 0;
                await Task.Run(() => _sftpClient.UploadFile(ms, destItemPath));
            }
        }
    }

    private async Task DeleteDirectoryRecursive(string directoryPath)
    {
        // List all items in directory
        var items = _sftpClient.ListDirectory(directoryPath);

        foreach (var item in items)
        {
            if (item.Name == "." || item.Name == "..") continue;

            var itemPath = $"{directoryPath}/{item.Name}";

            if (item.IsDirectory)
            {
                await DeleteDirectoryRecursive(itemPath);
            }
            else
            {
                await Task.Run(() => _sftpClient.DeleteFile(itemPath));
            }
        }

        // Delete the directory itself
        await Task.Run(() => _sftpClient.DeleteDirectory(directoryPath));
    }

    public async Task<OperationResult> DownloadItemAsync(string remotePath, string localPath, bool isFolder)
    {
        // Implementation for download operation would go here
        // This is a placeholder for the refactored download logic
        await Task.CompletedTask;
        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteItemAsync(string itemPath)
    {
        try
        {
            ProgressChanged?.Invoke(true);
            var resolvedPath = ResolveSftpPath(itemPath);

            LogRequested?.Invoke($"??? Deleting item: {resolvedPath}");

            // Check if the item is a file or directory
            var item = _sftpClient.Get(resolvedPath);

            if (item.IsDirectory)
            {
                LogRequested?.Invoke($"?? Deleting directory recursively: {Path.GetFileName(resolvedPath)}");
                await DeleteDirectoryRecursive(resolvedPath);
            }
            else
            {
                LogRequested?.Invoke($"?? Deleting file: {Path.GetFileName(resolvedPath)}");
                await Task.Run(() => _sftpClient.DeleteFile(resolvedPath));
            }

            LogRequested?.Invoke($"? Successfully deleted: {Path.GetFileName(resolvedPath)}");
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"? Failed to delete {Path.GetFileName(itemPath)}: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false);
        }
    }

    public async Task<OperationResult> DeleteMultiItemsAsync(List<string> itemPaths)
    {
        try
        {
            ProgressChanged?.Invoke(true);

            if (!itemPaths.Any())
            {
                LogRequested?.Invoke($"? Delete operation failed: No items provided");
                return OperationResult.Failure("No items provided");
            }

            var successCount = 0;
            var failCount = 0;
            var errors = new List<string>();

            LogRequested?.Invoke($"??? Starting delete operation for {itemPaths.Count} item(s)");

            foreach (var itemPath in itemPaths)
            {
                try
                {
                    var fileName = Path.GetFileName(itemPath);
                    var resolvedPath = ResolveSftpPath(itemPath);

                    LogRequested?.Invoke($"??? Deleting {fileName}");
                    var item = await Task.Run(() => _sftpClient.Get(resolvedPath));
                    // Check if source is a directory

                    if (item.IsDirectory)
                    {
                        await DeleteDirectoryRecursive(resolvedPath);
                    }
                    else
                    {
                        await Task.Run(() => _sftpClient.DeleteFile(resolvedPath));
                    }

                    successCount++;
                    LogRequested?.Invoke($"? Successfully deleted {fileName}");
                }
                catch (Exception ex)
                {
                    failCount++;
                    var fileName = Path.GetFileName(itemPath);
                    var errorMsg = $"Failed to delete {fileName}: {ex.Message}";
                    errors.Add(errorMsg);
                    LogRequested?.Invoke($"? {errorMsg}");
                }
            }

            var summary = $"Delete operation completed: {successCount} successful, {failCount} failed";
            LogRequested?.Invoke($"?? {summary}");

            if (failCount > 0)
            {
                return OperationResult.Failure($"{summary}. Errors: {string.Join("; ", errors)}");
            }

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"? Multi-delete operation failed: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false);
        }
    }

    private string ResolveSftpPath(string path)
    {
        if (string.IsNullOrEmpty(path) || _sftpClient?.WorkingDirectory == null)
            return path;
        return path.Replace("~", _sftpClient.WorkingDirectory);
    }
}

public class OperationResult
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public object? Data { get; private set; }

    private OperationResult(bool isSuccess, string? errorMessage = null, object? data = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Data = data;
    }

    public static OperationResult Success(object? data = null) => new(true, data: data);
    public static OperationResult Failure(string errorMessage) => new(false, errorMessage);
}