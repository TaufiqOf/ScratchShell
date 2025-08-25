using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Renci.SshNet;
using ScratchShell.Services.EventHandlers;
using ScratchShell.UserControls.BrowserControl;

namespace ScratchShell.Services.Navigation
{
    /// <summary>
    /// Manages SFTP navigation functionality including history, path resolution, and directory loading
    /// </summary>
    public class SftpNavigationManager : ISftpNavigationManager
    {
        private readonly ISftpLogger _logger;
        private readonly BrowserUserControl _browser;
        private readonly List<string> _navigationHistory = new();
        
        private SftpClient? _sftpClient;
        private PathTextBoxAdapter? _pathTextBox;
        private NavigationButtons? _buttons;
        private int _currentHistoryIndex = -1;
        private bool _isNavigatingFromHistory = false;
        private bool _isDirectoryLoading = false;

        public string CurrentPath => _pathTextBox?.Text?.Trim() ?? "/";
        public bool CanNavigateBack => _currentHistoryIndex > 0;
        public bool CanNavigateForward => _currentHistoryIndex < _navigationHistory.Count - 1;
        public bool IsAtRoot
        {
            get
            {
                var currentPath = CurrentPath;
                return string.IsNullOrEmpty(currentPath) || currentPath == "/" || currentPath == "~";
            }
        }

        public SftpNavigationManager(ISftpLogger logger, BrowserUserControl browser)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        }

        public void Initialize(SftpClient? client, PathTextBoxAdapter? pathTextBox, NavigationButtons? buttons)
        {
            _sftpClient = client;
            _pathTextBox = pathTextBox;
            _buttons = buttons;
            
            _navigationHistory.Clear();
            _currentHistoryIndex = -1;
        }

        public async Task GoToFolderAsync(string path)
        {
            if (_isDirectoryLoading)
            {
                _logger.LogWarning($"Directory loading already in progress, ignoring request for: {path}");
                return;
            }

            try
            {
                _isDirectoryLoading = true;
                var resolvedPath = ResolvePath(path);

                _logger.LogInfo($"Loading directory: {resolvedPath}");
                _browser.ShowProgress(true, $"Loading directory: {resolvedPath}");
                _browser.Clear();

                if (_pathTextBox != null)
                {
                    _pathTextBox.Text = resolvedPath;
                }

                AddToNavigationHistory(resolvedPath);
                await LoadDirectoryContentsAsync(resolvedPath);

                _logger.LogInfo($"Directory loaded successfully: {resolvedPath}");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError($"Access denied to directory {path}", ex);
                await TryReturnToPreviousDirectory();
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError($"Directory not found: {path}", ex);
                if (path != "/" && path != "~")
                {
                    _logger.LogInfo("Attempting to navigate to home directory");
                    await GoToFolderAsync("~");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading directory {path}", ex);
                throw;
            }
            finally
            {
                _isDirectoryLoading = false;
                _browser.ShowProgress(false);
                UpdateNavigationButtonStates();
            }
        }

        public async Task NavigateBackAsync()
        {
            if (!CanNavigateBack)
            {
                _logger.LogWarning("Cannot navigate back: No previous locations in history");
                return;
            }

            _currentHistoryIndex--;
            var previousPath = _navigationHistory[_currentHistoryIndex];
            _logger.LogInfo($"Navigating back to: {previousPath} (Index: {_currentHistoryIndex})");

            _isNavigatingFromHistory = true;
            await GoToFolderAsync(previousPath);
        }

        public async Task NavigateForwardAsync()
        {
            if (!CanNavigateForward)
            {
                _logger.LogWarning("Cannot navigate forward: No next locations in history");
                return;
            }

            _currentHistoryIndex++;
            var nextPath = _navigationHistory[_currentHistoryIndex];
            _logger.LogInfo($"Navigating forward to: {nextPath} (Index: {_currentHistoryIndex})");

            _isNavigatingFromHistory = true;
            await GoToFolderAsync(nextPath);
        }

        public async Task NavigateUpAsync()
        {
            if (IsAtRoot)
            {
                _logger.LogWarning("Cannot navigate up: Already at root");
                return;
            }

            var currentPath = CurrentPath;
            var parentPath = GetParentPath(currentPath);
            _logger.LogInfo($"Going up one level to: {parentPath}");
            await GoToFolderAsync(parentPath);
        }

        public async Task RefreshCurrentDirectoryAsync()
        {
            await GoToFolderAsync(CurrentPath);
        }

        public void UpdateNavigationButtonStates()
        {
            try
            {
                if (_buttons == null) return;

                var canGoBack = CanNavigateBack;
                var canGoForward = CanNavigateForward;
                var isAtRoot = IsAtRoot;

                if (_buttons.BackButton != null)
                {
                    _buttons.BackButton.IsEnabled = canGoBack;
                    UpdateBackButtonTooltip(canGoBack);
                }

                if (_buttons.ForwardButton != null)
                {
                    _buttons.ForwardButton.IsEnabled = canGoForward;
                    UpdateForwardButtonTooltip(canGoForward);
                }

                if (_buttons.UpButton != null)
                {
                    _buttons.UpButton.IsEnabled = !isAtRoot;
                    _buttons.UpButton.ToolTip = isAtRoot 
                        ? "Up one level (disabled - at root)" 
                        : "Up one level";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error updating navigation button states", ex);
            }
        }

        private void UpdateBackButtonTooltip(bool canGoBack)
        {
            if (_buttons?.BackButton == null) return;

            if (canGoBack)
            {
                var previousPath = _navigationHistory[_currentHistoryIndex - 1];
                var previousFolderName = Path.GetFileName(previousPath) ?? previousPath;
                if (string.IsNullOrEmpty(previousFolderName) || previousFolderName == "/")
                    previousFolderName = "Root";
                _buttons.BackButton.ToolTip = $"Back to '{previousFolderName}' (Alt+Left Arrow)";
            }
            else
            {
                _buttons.BackButton.ToolTip = "Back (disabled - no previous locations)";
            }
        }

        private void UpdateForwardButtonTooltip(bool canGoForward)
        {
            if (_buttons?.ForwardButton == null) return;

            if (canGoForward)
            {
                var nextPath = _navigationHistory[_currentHistoryIndex + 1];
                var nextFolderName = Path.GetFileName(nextPath) ?? nextPath;
                if (string.IsNullOrEmpty(nextFolderName) || nextFolderName == "/")
                    nextFolderName = "Root";
                _buttons.ForwardButton.ToolTip = $"Forward to '{nextFolderName}' (Alt+Right Arrow)";
            }
            else
            {
                _buttons.ForwardButton.ToolTip = "Forward (disabled - no next locations)";
            }
        }

        private async Task LoadDirectoryContentsAsync(string resolvedPath)
        {
            // Add parent folder entry
            _browser.AddItem(new BrowserItem
            {
                Name = "..",
                FullPath = $"{resolvedPath}/..",
                IsFolder = true,
                LastUpdated = DateTime.Now,
                Size = 0
            });

            int itemCount = 0;
            await foreach (var item in GetDirectoryContentsAsync(resolvedPath))
            {
                try
                {
                    _browser.AddItem(item);
                    itemCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error adding item {item?.Name}", ex);
                }
            }

            _logger.LogInfo($"Directory loaded: {itemCount} items found in {resolvedPath}");
        }

        private async IAsyncEnumerable<BrowserItem> GetDirectoryContentsAsync(
            string path,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_sftpClient == null)
            {
                _logger.LogError("SFTP client not available");
                yield break;
            }

            path = ResolveSftpPath(path);
            _logger.LogDebug($"Listing contents of: {path}");

            IAsyncEnumerable<Renci.SshNet.Sftp.ISftpFile> dirStream;

            try
            {
                dirStream = _sftpClient.ListDirectoryAsync(path, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to list directory {path}", ex);
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

        private string ResolvePath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    return "/";

                if (path == "~")
                {
                    return _sftpClient?.WorkingDirectory ?? "/";
                }

                if (path.Contains(".."))
                {
                    var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var resolvedParts = new List<string>();

                    foreach (var part in parts)
                    {
                        if (part == "..")
                        {
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

                    return resolvedParts.Count == 0 ? "/" : "/" + string.Join("/", resolvedParts);
                }

                if (!path.StartsWith("/"))
                {
                    path = "/" + path;
                }

                return path;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error resolving path {path}", ex);
                return "/";
            }
        }

        private string ResolveSftpPath(string path)
        {
            if (string.IsNullOrEmpty(path) || _sftpClient?.WorkingDirectory == null)
                return path;
            return path.Replace("~", _sftpClient.WorkingDirectory);
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
                if (lastSlashIndex == 0)
                {
                    return "/";
                }
                else if (lastSlashIndex > 0)
                {
                    return currentPath.Substring(0, lastSlashIndex);
                }

                return "/";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting parent path for {currentPath}", ex);
                return "/";
            }
        }

        private void AddToNavigationHistory(string path)
        {
            try
            {
                if (_isNavigatingFromHistory)
                {
                    _isNavigatingFromHistory = false;
                    return;
                }

                if (_navigationHistory.Count > 0 && _navigationHistory[_currentHistoryIndex] == path)
                {
                    return;
                }

                if (_currentHistoryIndex < _navigationHistory.Count - 1)
                {
                    _navigationHistory.RemoveRange(_currentHistoryIndex + 1, _navigationHistory.Count - _currentHistoryIndex - 1);
                }

                _navigationHistory.Add(path);
                _currentHistoryIndex = _navigationHistory.Count - 1;

                if (_navigationHistory.Count > 50)
                {
                    _navigationHistory.RemoveAt(0);
                    _currentHistoryIndex--;
                }

                _logger.LogDebug($"Added to navigation history: {path} (Index: {_currentHistoryIndex}, Total: {_navigationHistory.Count})");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error adding to navigation history", ex);
            }
        }

        private async Task TryReturnToPreviousDirectory()
        {
            if (_navigationHistory.Count > 1 && _currentHistoryIndex > 0)
            {
                _currentHistoryIndex--;
                var previousPath = _navigationHistory[_currentHistoryIndex];
                _logger.LogInfo($"Attempting to return to previous directory: {previousPath}");
                if (_pathTextBox != null)
                {
                    _pathTextBox.Text = previousPath;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                _navigationHistory?.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error disposing navigation manager", ex);
            }
        }
    }
}