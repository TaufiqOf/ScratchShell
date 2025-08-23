using Renci.SshNet;
using System.IO;
using System.Threading;

namespace ScratchShell.Services;

/// <summary>
/// Service for handling SFTP file operations with proper error handling and logging
/// </summary>
public interface ISftpFileOperationService
{
    Task<OperationResult> CreateFolderAsync(string folderName, string currentPath, CancellationToken cancellationToken = default);

    Task<OperationResult> RenameItemAsync(string oldPath, string newPath, bool isFolder, CancellationToken cancellationToken = default);

    Task<OperationResult> UploadFileAsync(string localFilePath, string remotePath, CancellationToken cancellationToken = default);

    Task<OperationResult> UploadMultipleFilesAsync(string[] localFilePaths, string remoteDirectory, CancellationToken cancellationToken = default);

    Task<OperationResult> PasteItemAsync(string destinationPath, CancellationToken cancellationToken = default);

    Task<OperationResult> PasteMultiItemsAsync(string destinationPath, CancellationToken cancellationToken = default);

    Task<OperationResult> DownloadItemAsync(string remotePath, string localPath, bool isFolder, CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteItemAsync(string itemPath, CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteMultiItemsAsync(List<string> itemPaths, CancellationToken cancellationToken = default);

    void UpdateClipboard(string itemPath, bool isCut);

    void UpdateMultiClipboard(List<string> itemPaths, bool isCut);

    bool HasClipboardContent { get; }
    bool HasMultipleClipboardItems { get; }
    string? ClipboardPath { get; }
    List<string> ClipboardPaths { get; }
    bool IsClipboardCut { get; }

    event Action<string>? LogRequested;

    event Action<bool, string, int?, int?>? ProgressChanged; // Updated to include current and total counts

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

    public event Action<bool, string, int?, int?>? ProgressChanged;

    public event Action? ClipboardStateChanged;

    public SftpFileOperationService(SftpClient sftpClient)
    {
        _sftpClient = sftpClient ?? throw new ArgumentNullException(nameof(sftpClient));
    }

    public async Task<OperationResult> CreateFolderAsync(string folderName, string currentPath, CancellationToken cancellationToken = default)
    {
        try
        {
            ProgressChanged?.Invoke(true, $"📁 Creating folder '{folderName}'...", null, null);
            var newFolderPath = $"{currentPath}/{folderName}";
            newFolderPath = ResolveSftpPath(newFolderPath);

            LogRequested?.Invoke($"📁 Creating new folder '{folderName}' in current directory");
            LogRequested?.Invoke($"📍 Full path: {newFolderPath}");

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => _sftpClient.CreateDirectory(newFolderPath), cancellationToken);

            LogRequested?.Invoke($"✅ Successfully created folder '{folderName}'");
            return OperationResult.Success(newFolderPath);
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke($"🚫 Folder creation cancelled by user");
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"❌ Failed to create folder '{folderName}': {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false, "", null, null);
        }
    }

    public async Task<OperationResult> RenameItemAsync(string oldPath, string newPath, bool isFolder, CancellationToken cancellationToken = default)
    {
        try
        {
            ProgressChanged?.Invoke(true, $"✏️ Renaming {(isFolder ? "folder" : "file")}...", null, null);
            var resolvedOldPath = ResolveSftpPath(oldPath);

            LogRequested?.Invoke($"✏️ Renaming {(isFolder ? "folder" : "file")}");
            LogRequested?.Invoke($"📍 Old path: {resolvedOldPath}");
            LogRequested?.Invoke($"📍 New path: {newPath}");

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => _sftpClient.RenameFile(resolvedOldPath, newPath), cancellationToken);

            LogRequested?.Invoke($"✅ Successfully renamed item");
            return OperationResult.Success(newPath);
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke($"🚫 Rename operation cancelled by user");
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"❌ Failed to rename item: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false, "", null, null);
        }
    }

    public async Task<OperationResult> UploadFileAsync(string localFilePath, string remotePath, CancellationToken cancellationToken = default)
    {
        try
        {
            ProgressChanged?.Invoke(true, $"⬆️ Uploading {Path.GetFileName(localFilePath)}...", null, null);
            remotePath = ResolveSftpPath(remotePath);
            var fileInfo = new FileInfo(localFilePath);

            LogRequested?.Invoke($"📤 Selected file for upload: {localFilePath} ({fileInfo.Length:N0} bytes)");
            LogRequested?.Invoke($"📍 Upload destination: {remotePath}");

            LogRequested?.Invoke($"🔍 Initial cancellation check - IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
            cancellationToken.ThrowIfCancellationRequested();

            // Add some artificial delay to make cancellation easier to test
            LogRequested?.Invoke($"🔍 Adding 2-second delay to test cancellation...");
            await Task.Delay(2000, cancellationToken);

            LogRequested?.Invoke($"🔍 After delay cancellation check - IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
            cancellationToken.ThrowIfCancellationRequested();

            using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);
            await Task.Run(() => _sftpClient.UploadFile(fs, remotePath), cancellationToken);

            LogRequested?.Invoke($"✅ Successfully uploaded {Path.GetFileName(localFilePath)} to {remotePath}");
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke($"🚫 Upload operation cancelled by user");
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"❌ Failed to upload {localFilePath}: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false, "", null, null);
        }
    }

    public async Task<OperationResult> UploadMultipleFilesAsync(string[] localFilePaths, string remoteDirectory, CancellationToken cancellationToken = default)
    {
        try
        {
            ProgressChanged?.Invoke(true, $"⬆️ Preparing upload of {localFilePaths.Length} item(s)...", null, localFilePaths.Length);
            remoteDirectory = ResolveSftpPath(remoteDirectory);

            LogRequested?.Invoke($"📤 Starting batch upload of {localFilePaths.Length} item(s)");
            LogRequested?.Invoke($"📍 Upload destination: {remoteDirectory}");

            var successCount = 0;
            var failCount = 0;
            var errors = new List<string>();
            var currentIndex = 0;

            foreach (var localPath in localFilePaths)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentIndex++;

                    var itemName = Path.GetFileName(localPath);
                    
                    if (Directory.Exists(localPath))
                    {
                        // Upload folder recursively
                        ProgressChanged?.Invoke(true, $"⬆️ Uploading folder '{itemName}' ({currentIndex} of {localFilePaths.Length})", currentIndex, localFilePaths.Length);
                        LogRequested?.Invoke($"📁 Uploading folder: {itemName}");
                        var remoteFolderPath = $"{remoteDirectory}/{itemName}";
                        await UploadDirectoryRecursive(localPath, remoteFolderPath, cancellationToken);
                        LogRequested?.Invoke($"✅ Successfully uploaded folder: {itemName}");
                    }
                    else if (File.Exists(localPath))
                    {
                        // Upload single file
                        var fileInfo = new FileInfo(localPath);
                        ProgressChanged?.Invoke(true, $"⬆️ Uploading file '{itemName}' ({currentIndex} of {localFilePaths.Length})", currentIndex, localFilePaths.Length);
                        LogRequested?.Invoke($"📄 Uploading file: {itemName} ({fileInfo.Length:N0} bytes)");
                        
                        var remoteFilePath = $"{remoteDirectory}/{itemName}";
                        
                        using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read);
                        await Task.Run(() => _sftpClient.UploadFile(fs, remoteFilePath), cancellationToken);
                        
                        LogRequested?.Invoke($"✅ Successfully uploaded file: {itemName}");
                    }
                    else
                    {
                        failCount++;
                        var errorMsg = $"Item not found: {itemName}";
                        errors.Add(errorMsg);
                        LogRequested?.Invoke($"❌ {errorMsg}");
                        continue;
                    }

                    successCount++;
                }
                catch (OperationCanceledException)
                {
                    LogRequested?.Invoke($"🚫 Batch upload operation cancelled by user");
                    return OperationResult.Failure("Operation was cancelled by user");
                }
                catch (Exception ex)
                {
                    failCount++;
                    var itemName = Path.GetFileName(localPath);
                    var errorMsg = $"Failed to upload {itemName}: {ex.Message}";
                    errors.Add(errorMsg);
                    LogRequested?.Invoke($"❌ {errorMsg}");
                }
            }

            var summary = $"Upload completed: {successCount} successful, {failCount} failed";
            LogRequested?.Invoke($"📊 {summary}");

            if (failCount > 0)
            {
                return OperationResult.Failure($"{summary}. Errors: {string.Join("; ", errors)}");
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke($"🚫 Batch upload operation cancelled by user");
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"❌ Batch upload operation failed: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false, "", null, null);
        }
    }

    private async Task UploadDirectoryRecursive(string localDirectoryPath, string remoteDirectoryPath, CancellationToken cancellationToken = default)
    {
        var directoryName = Path.GetFileName(localDirectoryPath);
        ProgressChanged?.Invoke(true, $"⬆️ Uploading folder {directoryName}...", null, null);

        LogRequested?.Invoke($"📁 Creating remote directory: {directoryName}");
        cancellationToken.ThrowIfCancellationRequested();

        // Create the remote directory
        await Task.Run(() => _sftpClient.CreateDirectory(remoteDirectoryPath), cancellationToken);

        // Get all files and subdirectories
        var files = Directory.GetFiles(localDirectoryPath);
        var subdirectories = Directory.GetDirectories(localDirectoryPath);

        // Upload all files
        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(filePath);
            var remoteFilePath = $"{remoteDirectoryPath}/{fileName}";
            var fileInfo = new FileInfo(filePath);

            ProgressChanged?.Invoke(true, $"⬆️ Uploading file {fileName}...", null, null);
            LogRequested?.Invoke($"📄 Uploading file: {fileName} ({fileInfo.Length:N0} bytes)");

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            await Task.Run(() => _sftpClient.UploadFile(fs, remoteFilePath), cancellationToken);

            LogRequested?.Invoke($"✅ Uploaded file: {fileName}");
        }

        // Recursively upload all subdirectories
        foreach (var subdirectoryPath in subdirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subdirectoryName = Path.GetFileName(subdirectoryPath);
            var remoteSubdirectoryPath = $"{remoteDirectoryPath}/{subdirectoryName}";

            LogRequested?.Invoke($"📁 Processing subdirectory: {subdirectoryName}");
            await UploadDirectoryRecursive(subdirectoryPath, remoteSubdirectoryPath, cancellationToken);
        }

        LogRequested?.Invoke($"✅ Completed uploading folder: {directoryName}");
    }

    public async Task<OperationResult> PasteItemAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        // If we have multiple items, use the multi-item paste method
        if (_clipboardPaths.Count > 1)
        {
            return await PasteMultiItemsAsync(destinationPath, cancellationToken);
        }

        try
        {
            var fileName = !string.IsNullOrEmpty(_clipboardPath) ? Path.GetFileName(_clipboardPath) : "item";
            var operation = _clipboardIsCut ? "🚚 Moving" : "📋 Copying";
            ProgressChanged?.Invoke(true, $"{operation} {fileName}...", null, null);

            if (string.IsNullOrEmpty(_clipboardPath))
            {
                LogRequested?.Invoke($"❌ Paste operation failed: Clipboard is empty");
                return OperationResult.Failure("Clipboard is empty");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var fullDestinationPath = $"{destinationPath}/{fileName}";
            var resolvedSourcePath = ResolveSftpPath(_clipboardPath);
            var resolvedDestinationPath = ResolveSftpPath(fullDestinationPath);

            LogRequested?.Invoke(_clipboardIsCut ?
                $"🚚 Moving file from {resolvedSourcePath} to {resolvedDestinationPath}" :
                $"📋 Copying file from {resolvedSourcePath} to {resolvedDestinationPath}");

            // Check if source is a directory
            var sourceFile = await Task.Run(() => _sftpClient.Get(resolvedSourcePath), cancellationToken);

            if (sourceFile.IsDirectory)
            {
                await CopyDirectoryRecursive(resolvedSourcePath, resolvedDestinationPath, cancellationToken);
            }
            else
            {
                using var ms = new MemoryStream();
                await Task.Run(() => _sftpClient.DownloadFile(resolvedSourcePath, ms), cancellationToken);
                ms.Position = 0;

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() => _sftpClient.UploadFile(ms, resolvedDestinationPath), cancellationToken);
            }

            if (_clipboardIsCut)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sourceFile.IsDirectory)
                {
                    await DeleteDirectoryRecursive(resolvedSourcePath, cancellationToken);
                }
                else
                {
                    await Task.Run(() => _sftpClient.DeleteFile(resolvedSourcePath), cancellationToken);
                }
                LogRequested?.Invoke($"✅ Successfully moved {resolvedSourcePath} to {resolvedDestinationPath}");
            }
            else
            {
                LogRequested?.Invoke($"✅ Successfully copied {resolvedSourcePath} to {resolvedDestinationPath}");
            }

            // Clear clipboard after operation
            _clipboardPath = null;
            _clipboardPaths.Clear();
            _clipboardIsCut = false;
            ClipboardStateChanged?.Invoke();

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke($"🚫 Paste operation cancelled by user");
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"❌ Paste operation failed: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false, "", null, null);
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
            $"✂️ Cut to clipboard: {itemPath}" :
            $"📋 Copied to clipboard: {itemPath}");
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
        LogRequested?.Invoke($"{(isCut ? "✂️" : "📋")} {operation} {itemCount} item(s) to clipboard");
    }

    public async Task<OperationResult> PasteMultiItemsAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var operation = _clipboardIsCut ? "🚚 Moving" : "📋 Copying";
            ProgressChanged?.Invoke(true, $"{operation} {_clipboardPaths.Count} items...", null, _clipboardPaths.Count);

            if (!_clipboardPaths.Any())
            {
                LogRequested?.Invoke($"❌ Paste operation failed: Clipboard is empty");
                return OperationResult.Failure("Clipboard is empty");
            }

            var successCount = 0;
            var failCount = 0;
            var errors = new List<string>();
            var currentIndex = 0;

            LogRequested?.Invoke($"🔄 Starting paste operation for {_clipboardPaths.Count} item(s)");

            foreach (var sourcePath in _clipboardPaths)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentIndex++;

                    var fileName = Path.GetFileName(sourcePath);
                    var fullDestinationPath = $"{destinationPath}/{fileName}";
                    var resolvedSourcePath = ResolveSftpPath(sourcePath);
                    var resolvedDestinationPath = ResolveSftpPath(fullDestinationPath);

                    var itemOperation = _clipboardIsCut ? "Moving" : "Copying";
                    ProgressChanged?.Invoke(true, $"{(_clipboardIsCut ? "🚚" : "📋")} {itemOperation} '{fileName}' ({currentIndex} of {_clipboardPaths.Count})", currentIndex, _clipboardPaths.Count);

                    LogRequested?.Invoke(_clipboardIsCut ?
                        $"🚚 Moving {fileName}" :
                        $"📋 Copying {fileName}");

                    // Check if source is a directory
                    var sourceFile = await Task.Run(() => _sftpClient.Get(resolvedSourcePath), cancellationToken);

                    if (sourceFile.IsDirectory)
                    {
                        await CopyDirectoryRecursive(resolvedSourcePath, resolvedDestinationPath, cancellationToken);
                    }
                    else
                    {
                        using var ms = new MemoryStream();
                        await Task.Run(() => _sftpClient.DownloadFile(resolvedSourcePath, ms), cancellationToken);
                        ms.Position = 0;

                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Run(() => _sftpClient.UploadFile(ms, resolvedDestinationPath), cancellationToken);
                    }

                    if (_clipboardIsCut)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (sourceFile.IsDirectory)
                        {
                            await DeleteDirectoryRecursive(resolvedSourcePath, cancellationToken);
                        }
                        else
                        {
                            await Task.Run(() => _sftpClient.DeleteFile(resolvedSourcePath), cancellationToken);
                        }
                    }

                    successCount++;
                    LogRequested?.Invoke($"✅ Successfully {(_clipboardIsCut ? "moved" : "copied")} {fileName}");
                }
                catch (OperationCanceledException)
                {
                    LogRequested?.Invoke($"🚫 Multi-paste operation cancelled by user");
                    return OperationResult.Failure("Operation was cancelled by user");
                }
                catch (Exception ex)
                {
                    failCount++;
                    var fileName = Path.GetFileName(sourcePath);
                    var errorMsg = $"Failed to {(_clipboardIsCut ? "move" : "copy")} {fileName}: {ex.Message}";
                    errors.Add(errorMsg);
                    LogRequested?.Invoke($"❌ {errorMsg}");
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
            LogRequested?.Invoke($"📊 {summary}");

            if (failCount > 0)
            {
                return OperationResult.Failure($"{summary}. Errors: {string.Join("; ", errors)}");
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke($"🚫 Multi-paste operation cancelled by user");
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"❌ Multi-paste operation failed: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false, "", null, null);
        }
    }

    public async Task<OperationResult> DownloadItemAsync(string remotePath, string localPath, bool isFolder, CancellationToken cancellationToken = default)
    {
        try
        {
            var itemName = Path.GetFileName(remotePath);
            ProgressChanged?.Invoke(true, $"⬇️ Downloading {itemName}...", null, null);

            var resolvedRemotePath = ResolveSftpPath(remotePath);

            LogRequested?.Invoke($"📥 Starting download operation");
            LogRequested?.Invoke($"🌐 Remote path: {resolvedRemotePath}");
            LogRequested?.Invoke($"💻 Local path: {localPath}");

            cancellationToken.ThrowIfCancellationRequested();

            if (isFolder)
            {
                LogRequested?.Invoke($"📁 Downloading folder recursively: {itemName}");

                // Create local directory if it doesn't exist
                Directory.CreateDirectory(localPath);

                await DownloadDirectoryRecursive(resolvedRemotePath, localPath, cancellationToken);

                LogRequested?.Invoke($"✅ Successfully downloaded folder '{itemName}' to {localPath}");
            }
            else
            {
                LogRequested?.Invoke($"📄 Downloading file: {itemName}");

                // Ensure the directory exists
                var directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write);
                await Task.Run(() => _sftpClient.DownloadFile(resolvedRemotePath, fs), cancellationToken);

                LogRequested?.Invoke($"✅ Successfully downloaded file '{itemName}' to {localPath}");
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke($"🚫 Download operation cancelled by user");
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"❌ Failed to download {Path.GetFileName(remotePath)}: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false, "", null, null);
        }
    }

    private async Task DownloadDirectoryRecursive(string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        var folderName = Path.GetFileName(remotePath);
        ProgressChanged?.Invoke(true, $"⬇️ Downloading folder {folderName}...", null, null);

        cancellationToken.ThrowIfCancellationRequested();

        // List all items in remote directory
        var items = await Task.Run(() => _sftpClient.ListDirectory(remotePath), cancellationToken);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Name == "." || item.Name == "..") continue;

            var remoteItemPath = $"{remotePath}/{item.Name}";
            var localItemPath = Path.Combine(localPath, item.Name);

            if (item.IsDirectory)
            {
                LogRequested?.Invoke($"📁 Creating local directory: {item.Name}");
                Directory.CreateDirectory(localItemPath);
                await DownloadDirectoryRecursive(remoteItemPath, localItemPath, cancellationToken);
            }
            else
            {
                ProgressChanged?.Invoke(true, $"⬇️ Downloading file {item.Name}...", null, null);
                LogRequested?.Invoke($"📄 Downloading file: {item.Name} ({item.Attributes.Size:N0} bytes)");

                cancellationToken.ThrowIfCancellationRequested();

                using var fs = new FileStream(localItemPath, FileMode.Create, FileAccess.Write);
                await Task.Run(() => _sftpClient.DownloadFile(remoteItemPath, fs), cancellationToken);

                LogRequested?.Invoke($"✅ Downloaded: {item.Name}");
            }
        }
    }

    public async Task<OperationResult> DeleteItemAsync(string itemPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileName = Path.GetFileName(itemPath);
            ProgressChanged?.Invoke(true, $"🗑️ Deleting {fileName}...", null, null);
            var resolvedPath = ResolveSftpPath(itemPath);

            LogRequested?.Invoke($"🗑️ Deleting item: {resolvedPath}");

            cancellationToken.ThrowIfCancellationRequested();

            // Check if the item is a file or directory
            var item = await Task.Run(() => _sftpClient.Get(resolvedPath), cancellationToken);

            if (item.IsDirectory)
            {
                LogRequested?.Invoke($"📁 Deleting directory recursively: {Path.GetFileName(resolvedPath)}");
                await DeleteDirectoryRecursive(resolvedPath, cancellationToken);
            }
            else
            {
                LogRequested?.Invoke($"📄 Deleting file: {Path.GetFileName(resolvedPath)}");
                await Task.Run(() => _sftpClient.DeleteFile(resolvedPath), cancellationToken);
            }

            LogRequested?.Invoke($"✅ Successfully deleted: {Path.GetFileName(resolvedPath)}");
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke($"🚫 Delete operation cancelled by user");
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"❌ Failed to delete {Path.GetFileName(itemPath)}: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false, "", null, null);
        }
    }

    public async Task<OperationResult> DeleteMultiItemsAsync(List<string> itemPaths, CancellationToken cancellationToken = default)
    {
        try
        {
            ProgressChanged?.Invoke(true, $"🗑️ Preparing to delete {itemPaths.Count} items...", null, itemPaths.Count);

            if (!itemPaths.Any())
            {
                LogRequested?.Invoke($"❌ Delete operation failed: No items provided");
                return OperationResult.Failure("No items provided");
            }

            var successCount = 0;
            var failCount = 0;
            var errors = new List<string>();
            var currentIndex = 0;

            LogRequested?.Invoke($"🗑️ Starting delete operation for {itemPaths.Count} item(s)");

            foreach (var itemPath in itemPaths)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentIndex++;

                    var fileName = Path.GetFileName(itemPath);
                    var resolvedPath = ResolveSftpPath(itemPath);

                    ProgressChanged?.Invoke(true, $"🗑️ Deleting '{fileName}' ({currentIndex} of {itemPaths.Count})", currentIndex, itemPaths.Count);

                    LogRequested?.Invoke($"🗑️ Deleting {fileName}");
                    var item = await Task.Run(() => _sftpClient.Get(resolvedPath), cancellationToken);
                    // Check if source is a directory

                    if (item.IsDirectory)
                    {
                        await DeleteDirectoryRecursive(resolvedPath, cancellationToken);
                    }
                    else
                    {
                        await Task.Run(() => _sftpClient.DeleteFile(resolvedPath), cancellationToken);
                    }

                    successCount++;
                    LogRequested?.Invoke($"✅ Successfully deleted {fileName}");
                }
                catch (OperationCanceledException)
                {
                    LogRequested?.Invoke($"🚫 Multi-delete operation cancelled by user");
                    return OperationResult.Failure("Operation was cancelled by user");
                }
                catch (Exception ex)
                {
                    failCount++;
                    var fileName = Path.GetFileName(itemPath);
                    var errorMsg = $"Failed to delete {fileName}: {ex.Message}";
                    errors.Add(errorMsg);
                    LogRequested?.Invoke($"❌ {errorMsg}");
                }
            }

            var summary = $"Delete operation completed: {successCount} successful, {failCount} failed";
            LogRequested?.Invoke($"📊 {summary}");

            if (failCount > 0)
            {
                return OperationResult.Failure($"{summary}. Errors: {string.Join("; ", errors)}");
            }

            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke($"🚫 Multi-delete operation cancelled by user");
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"❌ Multi-delete operation failed: {ex.Message}");
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            ProgressChanged?.Invoke(false, "", null, null);
        }
    }

    private async Task CopyDirectoryRecursive(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var folderName = Path.GetFileName(destinationPath);
        var operation = _clipboardIsCut ? "🚚 Moving" : "📋 Copying";
        ProgressChanged?.Invoke(true, $"{operation} folder {folderName}...", null, null);

        LogRequested?.Invoke($"{operation} directory: {sourcePath} → {destinationPath}");

        cancellationToken.ThrowIfCancellationRequested();

        // Create destination directory
        LogRequested?.Invoke($"📁 Creating destination directory: {folderName}");
        await Task.Run(() => _sftpClient.CreateDirectory(destinationPath), cancellationToken);

        // List all items in source directory
        var items = await Task.Run(() => _sftpClient.ListDirectory(sourcePath), cancellationToken);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Name == "." || item.Name == "..") continue;

            var sourceItemPath = $"{sourcePath}/{item.Name}";
            var destItemPath = $"{destinationPath}/{item.Name}";

            if (item.IsDirectory)
            {
                LogRequested?.Invoke($"📁 Processing subdirectory: {item.Name}");
                await CopyDirectoryRecursive(sourceItemPath, destItemPath, cancellationToken);
            }
            else
            {
                var fileOperation = _clipboardIsCut ? "🚚 Moving" : "📋 Copying";
                ProgressChanged?.Invoke(true, $"{fileOperation} file {item.Name}...", null, null);
                LogRequested?.Invoke($"{fileOperation} file: {item.Name} ({item.Attributes.Size:N0} bytes)");

                cancellationToken.ThrowIfCancellationRequested();

                using var ms = new MemoryStream();
                await Task.Run(() => _sftpClient.DownloadFile(sourceItemPath, ms), cancellationToken);
                ms.Position = 0;

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() => _sftpClient.UploadFile(ms, destItemPath), cancellationToken);

                LogRequested?.Invoke($"✅ {(_clipboardIsCut ? "Moved" : "Copied")} file: {item.Name}");
            }
        }

        LogRequested?.Invoke($"✅ {(_clipboardIsCut ? "Moved" : "Copied")} directory: {folderName}");
    }

    private async Task DeleteDirectoryRecursive(string directoryPath, CancellationToken cancellationToken = default)
    {
        var folderName = Path.GetFileName(directoryPath);
        ProgressChanged?.Invoke(true, $"🗑️ Deleting folder {folderName}...", null, null);

        LogRequested?.Invoke($"📋 Listing contents of directory: {folderName}");
        LogRequested?.Invoke($"🔍 Cancellation check before listing directory - IsCancellationRequested: {cancellationToken.IsCancellationRequested}");

        cancellationToken.ThrowIfCancellationRequested();

        // List all items in directory
        var items = await Task.Run(() => _sftpClient.ListDirectory(directoryPath), cancellationToken);

        LogRequested?.Invoke($"📋 Found {items.Count()} items in directory {folderName}");

        foreach (var item in items)
        {
            LogRequested?.Invoke($"🔍 Cancellation check before processing {item.Name} - IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Name == "." || item.Name == "..") continue;

            var itemPath = $"{directoryPath}/{item.Name}";

            if (item.IsDirectory)
            {
                LogRequested?.Invoke($"📁 Recursively deleting subdirectory: {item.Name}");
                await DeleteDirectoryRecursive(itemPath, cancellationToken);
            }
            else
            {
                ProgressChanged?.Invoke(true, $"🗑️ Deleting file {item.Name}...", null, null);
                LogRequested?.Invoke($"📄 Deleting file: {item.Name}");

                LogRequested?.Invoke($"🔍 Cancellation check before deleting file {item.Name} - IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
                cancellationToken.ThrowIfCancellationRequested();

                await Task.Run(() => _sftpClient.DeleteFile(itemPath), cancellationToken);
                LogRequested?.Invoke($"✅ Deleted file: {item.Name}");
            }
        }

        LogRequested?.Invoke($"🔍 Cancellation check before deleting directory {folderName} - IsCancellationRequested: {cancellationToken.IsCancellationRequested}");
        cancellationToken.ThrowIfCancellationRequested();

        // Delete the directory itself
        LogRequested?.Invoke($"🗑️ Deleting empty directory: {folderName}");
        await Task.Run(() => _sftpClient.DeleteDirectory(directoryPath), cancellationToken);
        LogRequested?.Invoke($"✅ Deleted directory: {folderName}");
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