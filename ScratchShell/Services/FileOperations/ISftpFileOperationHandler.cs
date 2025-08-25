using System;
using System.Threading.Tasks;
using ScratchShell.Services;
using ScratchShell.UserControls.BrowserControl;

namespace ScratchShell.Services.FileOperations
{
    /// <summary>
    /// Interface for handling SFTP file operations
    /// </summary>
    public interface ISftpFileOperationHandler : IDisposable
    {
        void Initialize(ISftpFileOperationService? fileOperationService);
        
        Task HandleCutAsync(BrowserItem item);
        Task HandleCopyAsync(BrowserItem item);
        Task HandlePasteAsync(string currentDirectory);
        Task HandleUploadAsync(string currentDirectory);
        Task HandleDownloadAsync(BrowserItem item);
        Task HandleDeleteAsync(BrowserItem item);
        Task HandleCreateFolderAsync(string currentDirectory, string folderName);
        Task HandleRenameAsync(BrowserItem item, string newName);
        
        Task HandleMultiCopyAsync(List<BrowserItem> items);
        Task HandleMultiCutAsync(List<BrowserItem> items);
        Task HandleMultiDeleteAsync(List<BrowserItem> items);
        Task HandleDragDropUploadAsync(string[] files, string currentDirectory);
        
        bool HasClipboardContent { get; }
    }
}