using ScratchShell.UserControls.BrowserControl;
using ScratchShell.ViewModels.Models;
using ScratchShell.Views.Windows;
using ScratchShell.Views.Dialog;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Controls;
using ScratchShell.Services.EventHandlers;
using ScratchShell.Services.Connection;
using ScratchShell.Services.Navigation;
using ScratchShell.Services.FileOperations;
using ScratchShell.Services;
using System; // Added for Action/Func

namespace ScratchShell.UserControls;

/// <summary>
/// Main SFTP user control that orchestrates various components for SFTP operations
/// </summary>
public partial class SftpUserControl : UserControl, IWorkspaceControl
{
    private readonly TabItemViewModel _currentTab;
    private readonly ServerViewModel _server;
    private readonly IContentDialogService _contentDialogService;
    private bool _isInitialized = false;

    // Core components
    private readonly ISftpConnectionManager _connectionManager;
    private readonly ISftpNavigationManager _navigationManager;
    private readonly ISftpFileOperationHandler _fileOperationHandler;
    private readonly ISftpEventHandler _eventHandler;
    private readonly ISftpLogger _logger;
    private bool _isClosed = false;
    // UI adapter for path textbox
    private readonly PathTextBoxAdapter _pathAdapter;

    // Connection monitoring
    private DispatcherTimer? _connectionMonitorTimer;
    private bool _isReconnecting = false;
    private string? _lastKnownPath;
    private FullScreenWindow _FullScreen;

    public BrowserUserControl Browser { get; }

    public SftpUserControl(TabItemViewModel tab, IContentDialogService contentDialogService)
    {
        InitializeComponent();
        _currentTab = tab;
        _server = tab.Server;
        tab.Removed += TabRemoved;
        _contentDialogService = contentDialogService;

        Browser = new BrowserUserControl();
        BrowserContentControl.Content = Browser;

        // Initialize components
        _logger = new SftpLogger(UpdateTerminalText);
        _connectionManager = new SftpConnectionManager(_logger);
        _navigationManager = new SftpNavigationManager(_logger, Browser);
        _fileOperationHandler = new SftpFileOperationHandler(_logger, _contentDialogService, Browser);
        _eventHandler = new SftpEventHandler(_logger, _navigationManager, _fileOperationHandler);
        _pathAdapter = new PathTextBoxAdapter(PathAddressBar);

        // Show initial loading state immediately
        ShowInitialLoadingState();

        SetupEventHandlers();
        SetupBrowserEvents();
        SetupConnectionMonitoring();

        // Subscribe to language changes
        LocalizationManager.LanguageChanged += OnLanguageChanged;
    }

    private void TabRemoved()
    {
        _isClosed = true;
        this.Dispose();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Update any UI elements that might need refreshing when language changes
        // Most of the localized strings are used in logging which will use the new language automatically
        // The browser control already handles its own language changes
    }

    private void SetupConnectionMonitoring()
    {
        // Monitor connection every 30 seconds
        _connectionMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _connectionMonitorTimer.Tick += async (s, e) => await CheckConnectionHealth();
    }

    private async Task CheckConnectionHealth()
    {
        if (_isReconnecting || !_isInitialized)
            return;

        try
        {
            if (!_connectionManager.IsConnectionAlive())
            {
                _logger.LogError(LocalizationManager.GetString("Connection_HealthCheckFailed"), null);
                await HandleConnectionTimeout(LocalizationManager.GetString("Connection_HealthCheckFailed"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(LocalizationManager.GetString("Operation_HealthCheckError"), ex);
            await HandleConnectionTimeout(string.Format(LocalizationManager.GetString("Operation_ConnectionError"), ex.Message));
        }
    }

    private async Task HandleConnectionTimeout(string errorMessage)
    {
        if (_isReconnecting)
            return;

        try
        {
            _isReconnecting = true;
            _connectionMonitorTimer?.Stop();

            // Store current path for restoration after reconnection
            _lastKnownPath = _navigationManager.CurrentPath;
            if (_isClosed)
            {
                return;
            }
            // Show disconnected state
            SetUIConnectionState(false);
            Browser.ShowProgress(true, LocalizationManager.GetString("Connection_Lost"));
            _logger.LogError(string.Format(LocalizationManager.GetString("Operation_ConnectionTimeoutDetected"), errorMessage), null);

            // Show reconnection dialog
            var reconnectionDialog = new ReconnectionDialog(_contentDialogService, _server, errorMessage);
            var result = await reconnectionDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                // User chose to reconnect
                await AttemptReconnection();
            }
            else
            {
                // User chose to close tab
                await CloseCurrentTab();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(LocalizationManager.GetString("Connection_HandleTimeoutError"), ex);
            Browser.ShowProgress(false);
            SetUIConnectionState(false);
        }
        finally
        {
            _isReconnecting = false;
        }
    }

    private async Task AttemptReconnection()
    {
        try
        {
            Browser.ShowProgress(true, string.Format(LocalizationManager.GetString("Connection_Reconnecting"), _server.Name));
            _logger.LogInfo(string.Format(LocalizationManager.GetString("Connection_AttemptingTo"), _server.Name, _server.Host, _server.Port));

            await _connectionManager.ReconnectAsync();

            // Reinitialize file operations with new connection
            _fileOperationHandler.Initialize(_connectionManager.FileOperationService, _navigationManager);
            if (_connectionManager.FileOperationService != null)
            {
                _connectionManager.FileOperationService.ProgressChanged += FileOperationServiceProgressChanged;
                SubscribeFileOperationClipboardEvents();
            }

            // Reinitialize navigation with new connection
            _navigationManager.Initialize(_connectionManager.Client, _pathAdapter, GetNavigationButtons());

            // Try to restore the last known path, fallback to home directory
            string pathToNavigate = !string.IsNullOrEmpty(_lastKnownPath) ? _lastKnownPath : "~";
            await _navigationManager.GoToFolderAsync(pathToNavigate);

            // Reconnection successful
            EnableUIAfterConnection();
            Browser.ShowProgress(false);
            _connectionMonitorTimer?.Start();

            _logger.LogInfo(string.Format(LocalizationManager.GetString("Connection_SuccessfullyReconnected"), _server.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(LocalizationManager.GetString("Connection_ReconnectionFailed"), ex);
            Browser.ShowProgress(false);

            // Show error and ask user what to do next
            var errorDialog = new ReconnectionDialog(_contentDialogService, _server, string.Format(LocalizationManager.GetString("Connection_ReconnectionFailed"), ex.Message));
            var result = await errorDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                // Try reconnecting again
                await AttemptReconnection();
            }
            else
            {
                // Close tab
                await CloseCurrentTab();
            }
        }
    }

    private async Task CloseCurrentTab()
    {
        try
        {
            _logger.LogInfo(LocalizationManager.GetString("Operation_ClosingCurrentTab"));
            SessionService.RemoveSession(_currentTab);
        }
        catch (Exception ex)
        {
            _logger.LogError(LocalizationManager.GetString("Connection_ErrorClosingTab") ?? "Error closing current tab", ex);
        }
    }

    private async Task ExecuteWithTimeoutDetection(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (TimeoutException ex)
        {
            await HandleConnectionTimeout(string.Format(LocalizationManager.GetString("Operation_Timeout"), ex.Message));
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            await HandleConnectionTimeout(string.Format(LocalizationManager.GetString("Operation_NetworkError"), ex.Message));
        }
        catch (Renci.SshNet.Common.SshConnectionException ex)
        {
            await HandleConnectionTimeout(string.Format(LocalizationManager.GetString("Operation_SSHConnectionError"), ex.Message));
        }
        catch (Exception ex) when (ex.Message.Contains("timeout") || ex.Message.Contains("connection") || ex.Message.Contains("network"))
        {
            await HandleConnectionTimeout(string.Format(LocalizationManager.GetString("Operation_ConnectionError"), ex.Message));
        }
    }

    private async Task LoadControl()
    {
        try
        {
            if (_isInitialized) return;

            // Update loading message
            Browser.ShowProgress(true, string.Format(LocalizationManager.GetString("Connection_EstablishingConnection"), _server.Name));
            _logger.LogInfo(string.Format(LocalizationManager.GetString("Connection_AttemptingTo"), _server.Name, _server.Host, _server.Port));

            await ExecuteWithTimeoutDetection(async () => await _connectionManager.ConnectAsync(_server));

            // Update loading message for initialization
            Browser.ShowProgress(true, LocalizationManager.GetString("Connection_InitializingFileOps"));
            _fileOperationHandler.Initialize(_connectionManager.FileOperationService, _navigationManager);
            _connectionManager.FileOperationService.ProgressChanged += FileOperationServiceProgressChanged;
            SubscribeFileOperationClipboardEvents();

            // Update loading message for navigation setup
            Browser.ShowProgress(true, LocalizationManager.GetString("Connection_SettingUpNavigation"));
            _navigationManager.Initialize(_connectionManager.Client, _pathAdapter, GetNavigationButtons());

            // Update loading message for directory loading
            Browser.ShowProgress(true, LocalizationManager.GetString("Connection_LoadingHomeDirectory"));
            await ExecuteWithTimeoutDetection(async () => await _navigationManager.GoToFolderAsync("~"));

            // Connection successful - enable UI and hide progress
            EnableUIAfterConnection();
            Browser.ShowProgress(false);

            // Start connection monitoring
            _connectionMonitorTimer?.Start();

            _isInitialized = true;
            _logger.LogInfo(string.Format(LocalizationManager.GetString("Connection_SuccessfullyConnected"), _server.Name));
        }
        catch (Exception ex)
        {
            _logger.LogError(LocalizationManager.GetString("Connection_ErrorDuringControlLoad") ?? "Error during control load", ex);

            // Show error state in browser
            Browser.ShowProgress(false);
            Browser.Clear();

            // Add an error message item to the browser
            var errorItem = new BrowserItem
            {
                Name = LocalizationManager.GetString("Connection_Failed"),
                FullPath = "",
                IsFolder = false,
                LastUpdated = DateTime.Now,
                Size = 0
            };
            Browser.AddItem(errorItem);

            // Update path to show error
            _pathAdapter.Text = string.Format(LocalizationManager.GetString("Connection_FailedTo"), _server.Host, _server.Port);

            // Keep UI disabled on error
            SetUIConnectionState(false);
            Progress.IsIndeterminate = false;
        }
    }

    private void FileOperationServiceProgressChanged(bool arg1, string arg2, int? arg3, int? arg4)
    {
        Browser.ShowProgress(arg1, arg2, arg3, arg4);
    }

    private NavigationButtons GetNavigationButtons()
    {
        return new NavigationButtons
        {
            BackButton = BackButton,
            ForwardButton = ForwardButton,
            UpButton = UpButton,
            RefreshButton = RefreshButton,
            CreateFolderButton = CreateFolderButton,
            PasteButton = PasteButton,
            OptionsButton = OptionsButton,
            FullScreenButton = FullScreenButton,
            Progress = Progress
        };
    }

    private void EnableUIAfterConnection()
    {
        SetUIConnectionState(true);
        UpdateClipboardUIState(); // Ensure paste reflects current clipboard
        _logger.LogInfo(LocalizationManager.GetString("Operation_UIEnabled"));
    }

    private void UpdateTerminalText(string logMessage)
    {
        try
        {
            if (Terminal != null)
            {
                Terminal.Text += logMessage;
                if (Terminal.Parent is ScrollViewer scrollViewer)
                {
                    scrollViewer.ScrollToEnd();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{LocalizationManager.GetString("Terminal_UpdateError") ?? "Terminal update error"}: {ex.Message}");
        }
    }

    // === Clipboard UI integration ===
    private void SubscribeFileOperationClipboardEvents()
    {
        var svc = _connectionManager.FileOperationService;
        if (svc == null) return;
        svc.ClipboardStateChanged -= FileOperationClipboardStateChanged; // prevent duplicates
        svc.ClipboardStateChanged += FileOperationClipboardStateChanged;
        UpdateClipboardUIState();
    }

    private void FileOperationClipboardStateChanged()
    {
        Dispatcher.Invoke(UpdateClipboardUIState);
    }

    private void UpdateClipboardUIState()
    {
        var hasClipboard = _connectionManager.FileOperationService?.HasClipboardContent == true;
        PasteButton.IsEnabled = hasClipboard && _connectionManager.IsConnected;
        // Update empty space context menu so Paste appears when right-clicking background
        Browser.UpdateEmptySpaceContextMenu(hasClipboard);
    }

    #region UI Event Handlers

    private async void RefreshButtonClick(object sender, RoutedEventArgs e)
    {
        await _eventHandler.SafeExecuteAsync(async () =>
            await ExecuteWithTimeoutDetection(async () => await _navigationManager.RefreshCurrentDirectoryAsync()));
    }

    private async void BackButtonClick(object sender, RoutedEventArgs e)
    {
        await _eventHandler.SafeExecuteAsync(async () =>
            await ExecuteWithTimeoutDetection(async () => await _navigationManager.NavigateBackAsync()));
    }

    private async void ForwardButtonClick(object sender, RoutedEventArgs e)
    {
        await _eventHandler.SafeExecuteAsync(async () =>
            await ExecuteWithTimeoutDetection(async () => await _navigationManager.NavigateForwardAsync()));
    }

    private async void UpButtonClick(object sender, RoutedEventArgs e)
    {
        await _eventHandler.SafeExecuteAsync(async () =>
            await ExecuteWithTimeoutDetection(async () => await _navigationManager.NavigateUpAsync()));
    }

    private async void PasteButtonClick(object sender, RoutedEventArgs e)
    {
        await _eventHandler.SafeExecuteAsync(async () =>
            await ExecuteWithTimeoutDetection(async () => await _fileOperationHandler.HandlePasteAsync(_pathAdapter.Text)));
    }

    private void CreateFolderButtonClick(object sender, RoutedEventArgs e)
    {
        _eventHandler.SafeExecute(() => Browser.StartNewFolderCreation());
    }

    private void LogToggleButtonChecked(object sender, RoutedEventArgs e)
    {
        _eventHandler.SafeExecute(() =>
        {
            LogGrid.Visibility = Visibility.Visible;
            _logger.LogInfo(LocalizationManager.GetString("Operation_LogPanelOpened"));
        });
    }

    private void LogToggleButtonUnChecked(object sender, RoutedEventArgs e)
    {
        _eventHandler.SafeExecute(() =>
        {
            LogGrid.Visibility = Visibility.Collapsed;
            _logger.LogInfo(LocalizationManager.GetString("Operation_LogPanelClosed"));
        });
    }

    private void FullScreenButtonClick(object sender, RoutedEventArgs e)
    {
        _eventHandler.SafeExecute(() =>
        {
            _logger.LogInfo(LocalizationManager.GetString("Operation_EnteringFullscreen"));
            FullScreenButton.IsEnabled = false;
            BrowserContentControl.Content = null;
            _FullScreen = new FullScreenWindow(_contentDialogService, Browser, _server.Name);
            _FullScreen.Show();
            _FullScreen.Closed += FullScreenClosed;
        });
    }

    private void FullScreenClosed(object? sender, EventArgs e)
    {
        _eventHandler.SafeExecute(() =>
        {
            _logger.LogInfo(LocalizationManager.GetString("Operation_ExitingFullscreen"));
            BrowserContentControl.Content = Browser;
            FullScreenButton.IsEnabled = true;
        });
    }

    #endregion

    private void ShowInitialLoadingState()
    {
        // Show browser immediately with loading state
        Browser.ShowProgress(true, string.Format(LocalizationManager.GetString("Connection_ConnectingTo"), _server.Name));

        // Disable UI buttons during connection
        SetUIConnectionState(false);

        // Set path to show server info
        _pathAdapter.Text = string.Format(LocalizationManager.GetString("Connection_ConnectingTo"), $"{_server.Host}:{_server.Port}");

        _logger.LogInfo(string.Format(LocalizationManager.GetString("Connection_AttemptingTo"), _server.Name, _server.Host, _server.Port));
    }

    private void SetUIConnectionState(bool isConnected)
    {
        BackButton.IsEnabled = isConnected;
        ForwardButton.IsEnabled = false; // Will be enabled based on navigation history
        RefreshButton.IsEnabled = isConnected;
        UpButton.IsEnabled = isConnected;
        CreateFolderButton.IsEnabled = isConnected;
        PasteButton.IsEnabled = false; // Will be enabled based on clipboard content
        OptionsButton.IsEnabled = isConnected;
        FullScreenButton.IsEnabled = isConnected;
        PathAddressBar.IsEnabled = isConnected;

        // Update progress bar
        Progress.IsIndeterminate = !isConnected;
    }

    private void SetupEventHandlers()
    {
        this.Loaded += async (s, e) => await LoadControl();
        this.KeyDown += (s, e) => _eventHandler.HandleKeyDown(e, _navigationManager, _fileOperationHandler);

        // Setup breadcrumb address bar event
        PathAddressBar.PathChanged += async (s, e) =>
        {
            await _eventHandler.SafeExecuteAsync(async () =>
            {
                if (!string.IsNullOrEmpty(e.NewPath))
                {
                    await ExecuteWithTimeoutDetection(async () => await _navigationManager.GoToFolderAsync(e.NewPath));
                }
            });
        };
    }

    private void SetupBrowserEvents()
    {
        Browser.OnProgress += BrowserOnProgress;
        _eventHandler.SetupBrowserEvents(Browser, _pathAdapter, _navigationManager, _fileOperationHandler);
    }

    private void BrowserOnProgress(bool obj)
    {
        SetUIConnectionState(!obj);
    }

    public void Dispose()
    {
        try
        {
            var svc = _connectionManager.FileOperationService;
            if (svc != null)
            {
                svc.ClipboardStateChanged -= FileOperationClipboardStateChanged;
            }

            // Unsubscribe from language changes
            LocalizationManager.LanguageChanged -= OnLanguageChanged;

            _logger?.LogInfo(LocalizationManager.GetString("Operation_StartingCleanup"));
            _connectionMonitorTimer?.Stop();
            _connectionMonitorTimer = null;
            _connectionManager?.Dispose();
            _navigationManager?.Dispose();
            _fileOperationHandler?.Dispose();
            _FullScreen?.Close();
            Browser?.Clear();
            _logger?.LogInfo(LocalizationManager.GetString("Operation_CleanupCompleted"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(string.Format(LocalizationManager.GetString("Critical_ErrorInDispose") ?? "Critical error in Dispose: {0} - {1}", ex.GetType().Name, ex.Message));
        }
    }
}

