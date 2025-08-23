using ScratchShell.UserControls.BrowserControl;

namespace ScratchShell.Models;

/// <summary>
/// Represents the context for a browser operation (item-based or directory-based)
/// </summary>
public class BrowserOperationContext
{
    public BrowserOperationType OperationType { get; }
    public string CurrentDirectory { get; }
    public BrowserItem? TargetItem { get; }
    public string? AdditionalData { get; }

    private BrowserOperationContext(
        BrowserOperationType operationType,
        string currentDirectory,
        BrowserItem? targetItem = null,
        string? additionalData = null)
    {
        OperationType = operationType;
        CurrentDirectory = currentDirectory ?? throw new ArgumentNullException(nameof(currentDirectory));
        TargetItem = targetItem;
        AdditionalData = additionalData;
    }

    /// <summary>
    /// Creates context for operations on a specific item
    /// </summary>
    public static BrowserOperationContext ForItem(BrowserItem item, string currentDirectory, string? additionalData = null)
    {
        return new BrowserOperationContext(BrowserOperationType.ItemOperation, currentDirectory, item, additionalData);
    }

    /// <summary>
    /// Creates context for operations in the current directory (empty space operations)
    /// </summary>
    public static BrowserOperationContext ForDirectory(string currentDirectory, string? additionalData = null)
    {
        return new BrowserOperationContext(BrowserOperationType.DirectoryOperation, currentDirectory, null, additionalData);
    }

    /// <summary>
    /// Creates context for new folder creation
    /// </summary>
    public static BrowserOperationContext ForNewFolder(string currentDirectory, string folderName)
    {
        return new BrowserOperationContext(BrowserOperationType.NewFolderOperation, currentDirectory, null, folderName);
    }

    /// <summary>
    /// Creates context for rename operation
    /// </summary>
    public static BrowserOperationContext ForRename(BrowserItem item, string currentDirectory, string newName)
    {
        return new BrowserOperationContext(BrowserOperationType.RenameOperation, currentDirectory, item, newName);
    }

    /// <summary>
    /// Gets the effective target path for the operation
    /// </summary>
    public string GetTargetPath()
    {
        return TargetItem?.FullPath ?? CurrentDirectory;
    }

    /// <summary>
    /// Gets a descriptive name for logging purposes
    /// </summary>
    public string GetDisplayName()
    {
        return TargetItem?.Name ?? "current directory";
    }

    /// <summary>
    /// Checks if this is an operation on a folder
    /// </summary>
    public bool IsFolder => TargetItem?.IsFolder ?? true; // Directory operations are folder-like
}

public enum BrowserOperationType
{
    ItemOperation,
    DirectoryOperation,
    NewFolderOperation,
    RenameOperation
}