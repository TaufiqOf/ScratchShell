using Renci.SshNet;
using System.IO;
using System.Threading;
using System.IO.Compression; // Added for zip archive support
using System.Windows; // For dispatcher marshaling to UI thread

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

    private void RaiseProgress(bool show, string message, int? current = null, int? total = null)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ProgressChanged?.Invoke(show, message, current, total));
        }
        else
        {
            ProgressChanged?.Invoke(show, message, current, total);
        }
    }

    private void RaiseLog(string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => LogRequested?.Invoke(message));
        }
        else
        {
            LogRequested?.Invoke(message);
        }
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
            if (localFilePaths == null || localFilePaths.Length == 0)
            {
                LogRequested?.Invoke("❌ No files provided for upload");
                return OperationResult.Failure("No files provided");
            }

            // If more than one item, or any item is a folder, create a zip archive and upload/extract remotely
            bool needsArchive = localFilePaths.Length > 1 || localFilePaths.Any(p => Directory.Exists(p));
            if (needsArchive)
            {
                return await UploadAsArchiveAsync(localFilePaths, remoteDirectory, cancellationToken);
            }

            // Fallback to single file path logic (should rarely land here because caller may use UploadFileAsync)
            if (localFilePaths.Length == 1 && File.Exists(localFilePaths[0]))
            {
                var singlePath = localFilePaths[0];
                var remotePath = ResolveSftpPath(Path.Combine(remoteDirectory, Path.GetFileName(singlePath)).Replace('\\', '/'));
                return await UploadFileAsync(singlePath, remotePath, cancellationToken);
            }

            return OperationResult.Failure("Unsupported upload selection");
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke("🚫 Batch upload operation cancelled by user");
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

    private async Task<OperationResult> UploadAsArchiveAsync(string[] localFilePaths, string remoteDirectory, CancellationToken cancellationToken)
    {
        string resolvedRemoteDir = ResolveSftpPath(remoteDirectory);
        string archiveBaseName = GenerateArchiveName(localFilePaths);
        string tempZipPath = Path.Combine(Path.GetTempPath(), archiveBaseName + ".zip");
        string remoteZipPath = resolvedRemoteDir.TrimEnd('/') + "/" + archiveBaseName + ".zip";

        // Pre-count total files and folders for user-friendly progress text
        (int totalFiles, int totalFolders) = CountItems(localFilePaths, cancellationToken);

        try
        {
            RaiseProgress(true, "🗜️ Creating upload archive...");
            RaiseLog($"🗜️ Creating temporary archive: {tempZipPath}");
            RaiseLog($"📊 Items to copy: {totalFiles} file(s), {totalFolders} folder(s)");

            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

            // Offload CPU-bound archive creation to background thread to keep UI responsive
            await Task.Run(() =>
            {
                using var zipStream = new FileStream(tempZipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true);

                int index = 0;
                foreach (var path in localFilePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    index++;
                    var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    RaiseProgress(true, $"🗜️ Adding '{name}' to archive ({index} of {localFilePaths.Length})", index, localFilePaths.Length);
                    if (Directory.Exists(path))
                    {
                        AddDirectoryToArchive(archive, path, name, cancellationToken);
                    }
                    else if (File.Exists(path))
                    {
                        AddFileToArchive(archive, path, name, cancellationToken);
                    }
                    else
                    {
                        RaiseLog($"⚠️ Skipping missing item: {path}");
                    }
                }
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Upload the archive (describe real intent to user)
            RaiseProgress(true, $"⬆️ Copying {totalFiles} file(s) and {totalFolders} folder(s)...");
            var fileInfo = new FileInfo(tempZipPath);
            RaiseLog($"📦 Archive size: {fileInfo.Length:N0} bytes (compressed)");
            using (var local = new FileStream(tempZipPath, FileMode.Open, FileAccess.Read))
            {
                await Task.Run(() => _sftpClient.UploadFile(local, remoteZipPath), cancellationToken);
            }
            RaiseLog($"✅ Uploaded archive to {remoteZipPath}");

            // Extract remotely using SSH (assumes 'unzip' installed).
            RaiseProgress(true, "📦 Extracting items on server...");
            bool extractionSucceeded = false;
            await Task.Run(() =>
            {
                using (var ssh = new SshClient(_sftpClient.ConnectionInfo))
                {
                    ssh.Connect();
                    string remoteFileName = Path.GetFileName(remoteZipPath);
                    string extractCmd = $"cd '{resolvedRemoteDir}' && (unzip -oq '{remoteFileName}' || (command -v busybox >/dev/null 2>&1 && busybox unzip -oq '{remoteFileName}') || echo 'UNZIP_FAILED')";
                    var cmd = ssh.CreateCommand(extractCmd);
                    var result = cmd.Execute();
                    extractionSucceeded = cmd.ExitStatus == 0 && !result.Contains("UNZIP_FAILED");
                    if (!extractionSucceeded)
                    {
                        RaiseLog($"❌ Remote unzip failed (exit {cmd.ExitStatus}). Output: {result} Error: {cmd.Error}");
                    }

                    // Always attempt to remove remote archive now per new requirement
                    var cleanup = ssh.CreateCommand($"cd '{resolvedRemoteDir}' && rm -f '{remoteFileName}'");
                    cleanup.Execute();
                    if (cleanup.ExitStatus == 0)
                        RaiseLog("🧹 Removed remote archive file");
                    else
                        RaiseLog($"⚠️ Failed to remove remote archive: {cleanup.Error}");
                    ssh.Disconnect();
                }
            }, cancellationToken);
            if (!extractionSucceeded)
            {
                return OperationResult.Failure("Remote extraction failed");
            }

            RaiseProgress(true, "✅ Copy complete");
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            RaiseLog("🚫 Upload operation cancelled by user");
            try
            {
                if (_sftpClient.Exists(remoteZipPath))
                {
                    _sftpClient.DeleteFile(remoteZipPath);
                    RaiseLog("🧹 Removed partially uploaded remote archive");
                }
            }
            catch { }
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            RaiseLog($"❌ Archive upload failed: {ex.Message}");
            try
            {
                if (_sftpClient.Exists(remoteZipPath))
                {
                    _sftpClient.DeleteFile(remoteZipPath);
                    RaiseLog("🧹 Removed remote archive after failure");
                }
            }
            catch { }
            return OperationResult.Failure(ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
            }
            catch { }
            RaiseProgress(false, "");
        }
    }

    private (int files, int folders) CountItems(IEnumerable<string> paths, CancellationToken token)
    {
        int fileCount = 0;
        int folderCount = 0;
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                fileCount++;
            }
            else if (Directory.Exists(path))
            {
                folderCount++;
                try
                {
                    // Count all sub-files and sub-folders
                    foreach (var _ in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        fileCount++;
                    }
                    foreach (var _ in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        folderCount++;
                    }
                }
                catch (Exception ex)
                {
                    RaiseLog($"⚠️ Counting failed for '{path}': {ex.Message}");
                }
            }
        }
        return (fileCount, folderCount);
    }

    // === Helper methods for archive creation (reintroduced) ===
    private string GenerateArchiveName(string[] paths)
    {
        var first = Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(first)) first = "archive";
        string safe = new string(first.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        if (string.IsNullOrEmpty(safe)) safe = "archive";
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return safe + "_" + stamp;
    }

    private void AddDirectoryToArchive(ZipArchive archive, string directoryPath, string rootName, CancellationToken token)
    {
        foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(directoryPath, file);
            var entryName = Path.Combine(rootName, relative).Replace('\\', '/');
            AddFileToArchive(archive, file, entryName, token);
        }
        // Add empty directories explicitly so hierarchy preserved
        foreach (var dir in Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                var relativeDir = Path.GetRelativePath(directoryPath, dir).Replace('\\', '/') + '/';
                archive.CreateEntry(Path.Combine(rootName, relativeDir).Replace('\\', '/'));
            }
        }
    }

    private void AddFileToArchive(ZipArchive archive, string filePath, string entryName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var entryStream = entry.Open();
        fs.CopyTo(entryStream);
    }
    // === End helper methods ===

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

            // Attempt fast server-side cp/mv first
            var fastResult = await TryServerSideSingleAsync(resolvedSourcePath, resolvedDestinationPath, _clipboardIsCut, cancellationToken);
            if (fastResult.IsSuccess)
            {
                LogRequested?.Invoke(_clipboardIsCut ?
                    $"✅ Successfully moved {resolvedSourcePath} to {resolvedDestinationPath} (server-side)" :
                    $"✅ Successfully copied {resolvedSourcePath} to {resolvedDestinationPath} (server-side)");
            }
            else
            {
                // Removed direct ternary interpolation to avoid parsing issues
                var opWord = _clipboardIsCut ? "move" : "copy";
                LogRequested?.Invoke($"⚠️ Server-side {opWord} failed ({fastResult.ErrorMessage}); falling back to SFTP transfer");

                // Fallback legacy logic
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
                    var item = await Task.Run(() => _sftpClient.Get(resolvedSourcePath), cancellationToken);
                    if (item.IsDirectory)
                        await DeleteDirectoryRecursive(resolvedSourcePath, cancellationToken);
                    else
                        await Task.Run(() => _sftpClient.DeleteFile(resolvedSourcePath), cancellationToken);
                }
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

            cancellationToken.ThrowIfCancellationRequested();

            // Attempt single server-side bulk cp/mv using directory destination
            var resolvedDestDir = ResolveSftpPath(destinationPath);
            var resolvedSources = _clipboardPaths.Select(p => ResolveSftpPath(p)).ToList();
            var bulkResult = await TryServerSideBulkAsync(resolvedSources, resolvedDestDir, _clipboardIsCut, cancellationToken);
            if (!bulkResult.IsSuccess)
            {
                // Removed direct ternary interpolation to avoid parsing issues
                var opWordBulk = _clipboardIsCut ? "move" : "copy";
                LogRequested?.Invoke($"⚠️ Server-side bulk {opWordBulk} failed ({bulkResult.ErrorMessage}); falling back to per-item SFTP transfer");

                var successCount = 0;
                var failCount = 0;
                var errors = new List<string>();
                var currentIndex = 0;
                foreach (var sourcePath in resolvedSources)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        currentIndex++;
                        var fileName = Path.GetFileName(sourcePath);
                        var resolvedDestinationPath = ResolveSftpPath($"{destinationPath}/{fileName}");
                        ProgressChanged?.Invoke(true, $"{(_clipboardIsCut ? "🚚" : "📋")} {( _clipboardIsCut ? "Moving" : "Copying")} '{fileName}' ({currentIndex} of {_clipboardPaths.Count})", currentIndex, _clipboardPaths.Count);

                        var sourceFile = await Task.Run(() => _sftpClient.Get(sourcePath), cancellationToken);
                        if (sourceFile.IsDirectory)
                            await CopyDirectoryRecursive(sourcePath, resolvedDestinationPath, cancellationToken);
                        else
                        {
                            using var ms = new MemoryStream();
                            await Task.Run(() => _sftpClient.DownloadFile(sourcePath, ms), cancellationToken);
                            ms.Position = 0;
                            await Task.Run(() => _sftpClient.UploadFile(ms, resolvedDestinationPath), cancellationToken);
                        }
                        if (_clipboardIsCut)
                        {
                            var itm = await Task.Run(() => _sftpClient.Get(sourcePath), cancellationToken);
                            if (itm.IsDirectory)
                                await DeleteDirectoryRecursive(sourcePath, cancellationToken);
                            else
                                await Task.Run(() => _sftpClient.DeleteFile(sourcePath), cancellationToken);
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
                if (failCount > 0 && successCount == 0)
                {
                    return OperationResult.Failure($"Bulk operation failed: {string.Join("; ", errors)}");
                }
            }
            else
            {
                LogRequested?.Invoke($"✅ Successfully completed server-side bulk {(_clipboardIsCut ? "move" : "copy")} operation");
            }

            // Clear clipboard after operation (always for cut; for copy keep? design chooses to clear like typical file managers after paste)
            _clipboardPaths.Clear();
            _clipboardPath = null;
            _clipboardIsCut = false;
            ClipboardStateChanged?.Invoke();

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

            var item = await Task.Run(() => _sftpClient.Get(resolvedPath), cancellationToken);

            if (item.IsDirectory)
            {
                LogRequested?.Invoke($"📁 Deleting directory with rm -rf: {Path.GetFileName(resolvedPath)}");
                await FastDeleteDirectory(resolvedPath, cancellationToken);
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

    private async Task FastDeleteDirectory(string directoryPath, CancellationToken cancellationToken = default)
    {
        var folderName = Path.GetFileName(directoryPath);
        ProgressChanged?.Invoke(true, $"🗑️ Deleting folder {folderName}...", null, null);
        LogRequested?.Invoke($"🗑️ Using fast delete (rm -rf) for directory: {directoryPath}");

        cancellationToken.ThrowIfCancellationRequested();

        // Basic safety checks - never allow deleting root or empty paths
        if (string.IsNullOrWhiteSpace(directoryPath) || directoryPath == "/" || directoryPath == "~")
        {
            throw new InvalidOperationException("Refusing to delete root or empty directory path.");
        }

        // Ensure path is resolved for ~ expansion
        var resolvedPath = ResolveSftpPath(directoryPath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || resolvedPath == "/")
        {
            throw new InvalidOperationException("Resolved path is unsafe for deletion.");
        }

        // Escape quotes in path
        var safePath = resolvedPath.Replace("\"", "\\\"");

        await Task.Run(() =>
        {
            using var ssh = new SshClient(_sftpClient.ConnectionInfo);
            ssh.Connect();
            var cmd = ssh.CreateCommand($"rm -rf -- \"{safePath}\"");
            var result = cmd.Execute();
            if (cmd.ExitStatus != 0)
            {
                throw new Exception($"rm -rf failed (exit {cmd.ExitStatus}): {cmd.Error}");
            }
            ssh.Disconnect();
        }, cancellationToken);

        LogRequested?.Invoke($"✅ Deleted directory using rm -rf: {resolvedPath}");
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

            // Use optimized bulk rm -rf strategy
            LogRequested?.Invoke("⚡ Performing bulk delete with single rm -rf command");
            var bulkResult = await BulkFastDeleteAsync(itemPaths, cancellationToken);
            if (!bulkResult.IsSuccess)
            {
                // Fallback (optional) – for now just report failure
                return bulkResult;
            }

            LogRequested?.Invoke("✅ Bulk delete completed successfully");
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

    private async Task<OperationResult> BulkFastDeleteAsync(List<string> itemPaths, CancellationToken cancellationToken)
    {
        try
        {
            // Resolve and validate paths
            var resolved = new List<string>();
            foreach (var raw in itemPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ResolveSftpPath(raw);
                if (string.IsNullOrWhiteSpace(path) || path == "/" || path == "~")
                {
                    var msg = $"Refusing to delete unsafe path: '{raw}' -> '{path}'";
                    LogRequested?.Invoke("❌ " + msg);
                    return OperationResult.Failure(msg);
                }
                resolved.Add(path);
            }

            // Build rm -rf command
            // Escape any double quotes in paths
            var escapedArgs = resolved.Select(p => $"\"{p.Replace("\"", "\\\"")}\"");
            var command = "rm -rf -- " + string.Join(' ', escapedArgs);

            LogRequested?.Invoke("🗑️ Executing: " + command);
            ProgressChanged?.Invoke(true, $"🗑️ Deleting {resolved.Count} items...", null, resolved.Count);

            await Task.Run(() =>
            {
                using var ssh = new SshClient(_sftpClient.ConnectionInfo);
                ssh.Connect();
                var cmd = ssh.CreateCommand(command);
                var result = cmd.Execute();
                if (cmd.ExitStatus != 0)
                {
                    throw new Exception($"rm -rf failed (exit {cmd.ExitStatus}): {cmd.Error} {result}");
                }
                ssh.Disconnect();
            }, cancellationToken);

            LogRequested?.Invoke($"✅ rm -rf removed {resolved.Count} item(s)");
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            LogRequested?.Invoke("🚫 Bulk delete cancelled by user");
            return OperationResult.Failure("Operation was cancelled by user");
        }
        catch (Exception ex)
        {
            LogRequested?.Invoke($"❌ Bulk delete failed: {ex.Message}");
            return OperationResult.Failure(ex.Message);
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

    // Implement interface clipboard update methods (restored after refactor)
    public void UpdateClipboard(string itemPath, bool isCut)
    {
        _clipboardPath = itemPath;
        _clipboardPaths.Clear();
        _clipboardPaths.Add(itemPath);
        _clipboardIsCut = isCut;
        ClipboardStateChanged?.Invoke();
        LogRequested?.Invoke(isCut ? $"✂️ Cut to clipboard: {itemPath}" : $"📋 Copied to clipboard: {itemPath}");
    }

    public void UpdateMultiClipboard(List<string> itemPaths, bool isCut)
    {
        _clipboardPaths.Clear();
        if (itemPaths != null && itemPaths.Count > 0)
        {
            _clipboardPaths.AddRange(itemPaths);
            _clipboardPath = itemPaths[0];
        }
        else
        {
            _clipboardPath = null;
        }
        _clipboardIsCut = isCut;
        ClipboardStateChanged?.Invoke();
        var count = itemPaths?.Count ?? 0;
        LogRequested?.Invoke(isCut ? $"✂️ Cut {count} item(s) to clipboard" : $"📋 Copied {count} item(s) to clipboard");
    }

    // ===== Added helper methods for server-side cp/mv operations =====
    private async Task<OperationResult> TryServerSideSingleAsync(string source, string destination, bool isMove, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            // Determine destination parent directory to guard against self-copy into itself
            if (IsSubPathOf(destination, source))
            {
                return OperationResult.Failure("Destination is inside source path");
            }
            string cmdText;
            if (isMove)
            {
                cmdText = $"mv -- {Quote(source)} {Quote(destination)}";
            }
            else
            {
                cmdText = $"cp -a -- {Quote(source)} {Quote(destination)}";
            }
            return await RunSshCommandAsync(cmdText, token);
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private async Task<OperationResult> TryServerSideBulkAsync(List<string> sources, string destinationDir, bool isMove, CancellationToken token)
    {
        if (!sources.Any()) return OperationResult.Failure("No sources");
        try
        {
            token.ThrowIfCancellationRequested();
            // basic safety
            if (string.IsNullOrWhiteSpace(destinationDir)) return OperationResult.Failure("Invalid destination");
            if (sources.Any(s => IsSubPathOf(destinationDir, s)))
            {
                return OperationResult.Failure("Destination is inside a source path");
            }
            var joinedSources = string.Join(' ', sources.Select(Quote));
            string cmdText = isMove ? $"mv -- {joinedSources} {Quote(destinationDir)}" : $"cp -a -- {joinedSources} {Quote(destinationDir)}";
            return await RunSshCommandAsync(cmdText, token);
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ex.Message);
        }
    }

    private async Task<OperationResult> RunSshCommandAsync(string command, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            using var ssh = new SshClient(_sftpClient.ConnectionInfo);
            ssh.Connect();
            var cmd = ssh.CreateCommand(command);
            var result = cmd.Execute();
            var success = cmd.ExitStatus == 0;
            ssh.Disconnect();
            return success ? OperationResult.Success() : OperationResult.Failure($"Command failed (exit {cmd.ExitStatus}): {cmd.Error} {result}");
        }, token);
    }

    private string Quote(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";

    private bool IsSubPathOf(string potentialParent, string potentialChild)
    {
        var parent = potentialParent.TrimEnd('/') + "/";
        var child = potentialChild.TrimEnd('/') + "/";
        return child.StartsWith(parent, StringComparison.Ordinal);
    }
    // ===== End helper methods =====
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