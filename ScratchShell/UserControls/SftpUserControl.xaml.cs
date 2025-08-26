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
using ScratchShell.Services.EventHandlers;
using ScratchShell.Services.Connection;
using ScratchShell.Services.Navigation;
using ScratchShell.Services.FileOperations;

namespace ScratchShell.UserControls;

/// <summary>
/// Main SFTP user control that orchestrates various components for SFTP operations
/// </summary>
public partial class SftpUserControl : UserControl, IWorkspaceControl
{
    private readonly ServerViewModel _server;
    private readonly IContentDialogService _contentDialogService;
    private bool _isInitialized = false;

    // Core components
    private readonly ISftpConnectionManager _connectionManager;
    private readonly ISftpNavigationManager _navigationManager;
    private readonly ISftpFileOperationHandler _fileOperationHandler;
    private readonly ISftpEventHandler _eventHandler;
    private readonly ISftpLogger _logger;

    // UI adapter for path textbox
    private readonly PathTextBoxAdapter _pathAdapter;

    public BrowserUserControl Browser { get; }

    public SftpUserControl(ServerViewModel server, IContentDialogService contentDialogService)
    {
        InitializeComponent();
        _server = server;
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
    }

    private void ShowInitialLoadingState()
    {
        // Show browser immediately with loading state
        Browser.ShowProgress(true, $"Connecting to {_server.Name}...");
        
        // Disable UI buttons during connection
        SetUIConnectionState(false);
        
        // Set path to show server info
        _pathAdapter.Text = $"Connecting to {_server.Host}:{_server.Port}...";
        
        _logger.LogInfo($"Initializing connection to {_server.Name} ({_server.Host}:{_server.Port})");
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
                    await _navigationManager.GoToFolderAsync(e.NewPath);
                }
            });
        };
    }

    private void SetupBrowserEvents()
    {
        _eventHandler.SetupBrowserEvents(Browser, _pathAdapter, _navigationManager, _fileOperationHandler);
    }

    private async Task LoadControl()
    {
        try
        {
            if (_isInitialized) return;

            // Update loading message
            Browser.ShowProgress(true, $"Establishing connection to {_server.Name}...");
            _logger.LogInfo($"Attempting to connect to {_server.Name} ({_server.Host}:{_server.Port})");

            await _connectionManager.ConnectAsync(_server);
            
            // Update loading message for initialization
            Browser.ShowProgress(true, "Initializing file operations...");
            _fileOperationHandler.Initialize(_connectionManager.FileOperationService, _navigationManager);
            _connectionManager.FileOperationService.ProgressChanged += FileOperationServiceProgressChanged;
            // Update loading message for navigation setup
            Browser.ShowProgress(true, "Setting up navigation...");
            _navigationManager.Initialize(_connectionManager.Client, _pathAdapter, GetNavigationButtons());
            
            // Update loading message for directory loading
            Browser.ShowProgress(true, "Loading home directory...");
            await _navigationManager.GoToFolderAsync("~");
            
            // Connection successful - enable UI and hide progress
            EnableUIAfterConnection();
            Browser.ShowProgress(false);
            
            _isInitialized = true;
            _logger.LogInfo($"Successfully connected and initialized for {_server.Name}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during control load", ex);
            
            // Show error state in browser
            Browser.ShowProgress(false);
            Browser.Clear();
            
            // Add an error message item to the browser
            var errorItem = new BrowserItem
            {
                Name = "Connection Failed",
                FullPath = "",
                IsFolder = false,
                LastUpdated = DateTime.Now,
                Size = 0
            };
            Browser.AddItem(errorItem);
            
            // Update path to show error
            _pathAdapter.Text = $"Failed to connect to {_server.Host}:{_server.Port}";
            
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
        Browser.UpdateEmptySpaceContextMenu(false);
        _logger.LogInfo("User interface enabled - ready for file operations");
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
            System.Diagnostics.Debug.WriteLine($"Terminal update error: {ex.Message}");
        }
    }

    #region UI Event Handlers

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _eventHandler.SafeExecuteAsync(async () => 
            await _navigationManager.RefreshCurrentDirectoryAsync());
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        await _eventHandler.SafeExecuteAsync(async () => 
            await _navigationManager.NavigateBackAsync());
    }

    private async void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        await _eventHandler.SafeExecuteAsync(async () => 
            await _navigationManager.NavigateForwardAsync());
    }

    private async void UpButton_Click(object sender, RoutedEventArgs e)
    {
        await _eventHandler.SafeExecuteAsync(async () => 
            await _navigationManager.NavigateUpAsync());
    }

    private async void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        await _eventHandler.SafeExecuteAsync(async () =>
            await _fileOperationHandler.HandlePasteAsync(_pathAdapter.Text));
    }

    private void CreateFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _eventHandler.SafeExecute(() => Browser.StartNewFolderCreation());
    }

    private void LogToggleButtonChecked(object sender, RoutedEventArgs e)
    {
        _eventHandler.SafeExecute(() =>
        {
            LogGrid.Visibility = Visibility.Visible;
            _logger.LogInfo("Operation log panel opened");
        });
    }

    private void LogToggleButtonUnChecked(object sender, RoutedEventArgs e)
    {
        _eventHandler.SafeExecute(() =>
        {
            LogGrid.Visibility = Visibility.Collapsed;
            _logger.LogInfo("Operation log panel closed");
        });
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        _eventHandler.SafeExecute(() =>
        {
            _logger.LogInfo("Entering full screen mode");
            FullScreenButton.IsEnabled = false;
            BrowserContentControl.Content = null;
            var fullScreen = new FullScreenWindow(_contentDialogService, Browser, _server.Name);
            fullScreen.Show();
            fullScreen.Closed += (s, args) =>
            {
                _eventHandler.SafeExecute(() =>
                {
                    _logger.LogInfo("Exiting full screen mode");
                    BrowserContentControl.Content = Browser;
                    FullScreenButton.IsEnabled = true;
                });
            };
        });
    }

    #endregion

    public void Dispose()
    {
        try
        {
            _logger?.LogInfo("Starting cleanup and disconnection");
            _connectionManager?.Dispose();
            _navigationManager?.Dispose();
            _fileOperationHandler?.Dispose();
            Browser?.Clear();
            _logger?.LogInfo("Cleanup completed successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Critical error in Dispose: {ex.GetType().Name} - {ex.Message}");
        }
    }
}

