using System.Windows.Input;
using ScratchShell.Services.FileOperations;
using ScratchShell.Services.Navigation;
using ScratchShell.UserControls.BrowserControl;
using ScratchShell.Resources;

namespace ScratchShell.Services.EventHandlers
{
    /// <summary>
    /// Handles SFTP events with comprehensive error handling and safe execution
    /// </summary>
    public class SftpEventHandler : ISftpEventHandler
    {
        private readonly ISftpLogger _logger;
        private readonly ISftpNavigationManager _navigationManager;
        private readonly ISftpFileOperationHandler _fileOperationHandler;

        public SftpEventHandler(ISftpLogger logger, ISftpNavigationManager navigationManager, ISftpFileOperationHandler fileOperationHandler)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _navigationManager = navigationManager ?? throw new ArgumentNullException(nameof(navigationManager));
            _fileOperationHandler = fileOperationHandler ?? throw new ArgumentNullException(nameof(fileOperationHandler));
        }

        public void SetupBrowserEvents(BrowserUserControl browser, PathTextBoxAdapter pathTextBox, 
            ISftpNavigationManager navigationManager, ISftpFileOperationHandler fileOperationHandler)
        {
            try
            {
                // Browser navigation events
                browser.EnterRequested += SafeEventHandler<BrowserItem>(async item => 
                    await HandleBrowserEnterRequestedAsync(item, navigationManager));
                
                browser.ItemRenamed += SafeEventHandler<BrowserItem, string>(async (item, newName) => 
                    await fileOperationHandler.HandleRenameAsync(item, newName));
                
                browser.NewFolderCreated += SafeEventHandler<BrowserItem>(async item => 
                    await fileOperationHandler.HandleCreateFolderAsync(pathTextBox.Text, item.Name));
                
                browser.ItemEditCancelled += SafeEventHandler<BrowserItem>(item => 
                    _logger.LogInfo(string.Format(Langauge.EventHandler_EditCancelled, item.Name)));

                // File operation events
                browser.CutRequested += SafeEventHandler<BrowserItem>(async item => 
                    await fileOperationHandler.HandleCutAsync(item));
                
                browser.CopyRequested += SafeEventHandler<BrowserItem>(async item => 
                    await fileOperationHandler.HandleCopyAsync(item));
                
                browser.PasteRequested += SafeEventHandler<BrowserItem>(async item => 
                    await fileOperationHandler.HandlePasteAsync(pathTextBox.Text));
                
                browser.UploadRequested += SafeEventHandler<BrowserItem>(async item => 
                    await fileOperationHandler.HandleUploadAsync(pathTextBox.Text));
                
                browser.DownloadRequested += SafeEventHandler<BrowserItem>(async item => 
                    await fileOperationHandler.HandleDownloadAsync(item));
                
                browser.DeleteRequested += SafeEventHandler<BrowserItem>(async item => 
                    await fileOperationHandler.HandleDeleteAsync(item));

                // Empty space operations
                browser.EmptySpacePasteRequested += SafeEventHandler(async () => 
                    await fileOperationHandler.HandlePasteAsync(pathTextBox.Text));
                
                browser.EmptySpaceUploadRequested += SafeEventHandler(async () => 
                    await fileOperationHandler.HandleUploadAsync(pathTextBox.Text));
                
                browser.EmptySpaceNewFolderRequested += SafeEventHandler(() => 
                    browser.StartNewFolderCreation());

                // Multi-select operations
                browser.MultiCopyRequested += SafeEventHandler<List<BrowserItem>>(async items => 
                    await HandleMultiSelectOperation(items, async (validItems) => 
                        await fileOperationHandler.HandleMultiCopyAsync(validItems)));
                
                browser.MultiCutRequested += SafeEventHandler<List<BrowserItem>>(async items => 
                    await HandleMultiSelectOperation(items, async (validItems) => 
                        await fileOperationHandler.HandleMultiCutAsync(validItems)));
                
                browser.MultiDeleteRequested += SafeEventHandler<List<BrowserItem>>(async items => 
                    await HandleMultiSelectOperation(items, async (validItems) => 
                        await fileOperationHandler.HandleMultiDeleteAsync(validItems)));

                // Other events
                browser.RefreshRequested += SafeEventHandler(async () => 
                    await navigationManager.RefreshCurrentDirectoryAsync());
                
                browser.FilesDropped += SafeEventHandler<string[]>(async files => 
                    await fileOperationHandler.HandleDragDropUploadAsync(files, pathTextBox.Text));

                _logger.LogInfo(Langauge.EventHandler_BrowserEventsSetupSuccess);
            }
            catch (Exception ex)
            {
                _logger.LogError(Langauge.EventHandler_BrowserEventsSetupError, ex);
            }
        }

        public void HandleKeyDown(KeyEventArgs e, ISftpNavigationManager navigationManager, 
            ISftpFileOperationHandler fileOperationHandler)
        {
            SafeExecute(() =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Alt)
                {
                    switch (e.Key)
                    {
                        case Key.Left when CanNavigateBack(navigationManager):
                            _ = SafeExecuteAsync(async () => await navigationManager.NavigateBackAsync());
                            e.Handled = true;
                            break;

                        case Key.Right when CanNavigateForward(navigationManager):
                            _ = SafeExecuteAsync(async () => await navigationManager.NavigateForwardAsync());
                            e.Handled = true;
                            break;
                    }
                }
                else if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    switch (e.Key)
                    {
                        case Key.V when fileOperationHandler.HasClipboardContent:
                            _ = SafeExecuteAsync(async () => await fileOperationHandler.HandlePasteAsync(navigationManager.CurrentPath));
                            e.Handled = true;
                            break;

                        case Key.C:
                            // Handle multi-copy for selected items
                            // This would need access to the browser's selected items
                            e.Handled = true;
                            break;

                        case Key.X:
                            // Handle multi-cut for selected items
                            // This would need access to the browser's selected items
                            e.Handled = true;
                            break;
                    }
                }
            });
        }

        public void SafeExecute(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.LogError(Langauge.EventHandler_SafeExecuteError, ex);
                HandleEventException(ex, nameof(action));
            }
        }

        public async Task SafeExecuteAsync(Func<Task> asyncAction)
        {
            try
            {
                await asyncAction();
            }
            catch (Exception ex)
            {
                _logger.LogError(Langauge.EventHandler_SafeExecuteAsyncError, ex);
                HandleEventException(ex, nameof(asyncAction));
            }
        }

        private Action SafeEventHandler(Action handler)
        {
            return () => SafeExecute(handler);
        }

        private Action SafeEventHandler(Func<Task> handler)
        {
            return () => _ = SafeExecuteAsync(handler);
        }

        private Action<T> SafeEventHandler<T>(Action<T> handler)
        {
            return (arg) => SafeExecute(() => handler(arg));
        }

        private Action<T> SafeEventHandler<T>(Func<T, Task> handler)
        {
            return (arg) => _ = SafeExecuteAsync(async () => await handler(arg));
        }

        private Action<T1, T2> SafeEventHandler<T1, T2>(Action<T1, T2> handler)
        {
            return (arg1, arg2) => SafeExecute(() => handler(arg1, arg2));
        }

        private Action<T1, T2> SafeEventHandler<T1, T2>(Func<T1, T2, Task> handler)
        {
            return (arg1, arg2) => _ = SafeExecuteAsync(async () => await handler(arg1, arg2));
        }

        private async Task HandleBrowserEnterRequestedAsync(BrowserItem item, ISftpNavigationManager navigationManager)
        {
            if (item.IsFolder)
            {
                if (item.Name == "..")
                {
                    var parentPath = GetParentPath(navigationManager.CurrentPath);
                    _logger.LogInfo(string.Format(Langauge.Navigation_NavigatingToParent, parentPath));
                    await navigationManager.NavigateUpAsync();
                }
                else
                {
                    _logger.LogInfo(string.Format(Langauge.Navigation_NavigatingToFolder, item.FullPath));
                    await navigationManager.GoToFolderAsync(item.FullPath);
                }
            }
            else
            {
                _logger.LogInfo(string.Format(Langauge.Navigation_OpeningFile, item.Name));
            }
        }

        private async Task HandleMultiSelectOperation(List<BrowserItem> items, Func<List<BrowserItem>, Task> operation)
        {
            if (!items.Any())
            {
                _logger.LogWarning(Langauge.Navigation_MultiSelectOperationFailed);
                return;
            }

            var validItems = items.Where(item => item.Name != "..").ToList();
            if (!validItems.Any())
            {
                _logger.LogWarning(Langauge.Navigation_MultiSelectOperationFailedValid);
                return;
            }

            _logger.LogInfo(string.Format(Langauge.Navigation_MultiSelectOperation, validItems.Count));
            await operation(validItems);
        }

        private string GetParentPath(string currentPath)
        {
            try
            {
                if (string.IsNullOrEmpty(currentPath) || currentPath == "/" || currentPath == "~")
                {
                    return "/";
                }

                currentPath = currentPath.TrimEnd('/');
                if (!currentPath.Contains('/'))
                {
                    return "/";
                }

                var lastSlashIndex = currentPath.LastIndexOf('/');
                return lastSlashIndex == 0 ? "/" : currentPath.Substring(0, lastSlashIndex);
            }
            catch (Exception ex)
            {
                _logger.LogError(string.Format(Langauge.Navigation_ErrorGettingParentPath, currentPath), ex);
                return "/";
            }
        }

        private bool CanNavigateBack(ISftpNavigationManager navigationManager)
        {
            return navigationManager.CanNavigateBack;
        }

        private bool CanNavigateForward(ISftpNavigationManager navigationManager)
        {
            return navigationManager.CanNavigateForward;
        }

        private void HandleEventException(Exception ex, string handlerName)
        {
            try
            {
                _logger.LogError(string.Format(Langauge.EventHandler_ExceptionIn, handlerName), ex);
                
                // Additional error handling could go here:
                // - Reset UI state
                // - Cancel ongoing operations
                // - Show user-friendly error messages
            }
            catch (Exception innerEx)
            {
                System.Diagnostics.Debug.WriteLine(string.Format(Langauge.EventHandler_CriticalErrorInExceptionHandler, innerEx.Message));
            }
        }
    }
}