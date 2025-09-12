using Renci.SshNet;
using ScratchShell.Services;
using ScratchShell.Services.Navigation;
using ScratchShell.Services.Terminal;
using ScratchShell.UserControls.GTPTerminalControl;
using ScratchShell.View.Dialog;
using ScratchShell.ViewModels.Models;
using ScratchShell.Views.Windows;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using System.Net.Sockets; // Added for reconnection exception handling
using System.Windows; // Added for Visibility

namespace ScratchShell.UserControls;

/// <summary>
/// Interaction logic for SshUserControl.xaml
/// </summary>
public partial class SshUserControl : UserControl, IWorkspaceControl
{
    private ServerViewModel _server;
    private readonly IContentDialogService _contentDialogService;
    private SshClient? _sshClient;
    private ShellStream _shellStream;
    private bool _isInitialized = false;
    private FullScreenWindow _FullScreen;

    // AutoComplete services
    private ITerminalAutoCompleteService? _autoCompleteService;
    private IPathCompletionService _pathCompletionService;
    private string _currentWorkingDirectory = "~";

    public ITerminal Terminal { get; private set; }

    private bool _lastSelectionState = false;
    private bool _lastClipboardState = false;
    private bool _cachedClipboardState = false;
    private System.Windows.Threading.DispatcherTimer _clipboardTimer;

    // Reconnection / monitoring
    private DispatcherTimer? _connectionMonitorTimer; // monitors connection health
    private bool _isReconnecting = false;
    private string? _lastKnownWorkingDirectory; // store to restore after reconnection

    public SshUserControl(ServerViewModel server, IContentDialogService contentDialogService)
    {
        InitializeComponent();
        Terminal = new GPTTerminalUserControl();
        TerminalContentControl.Content = Terminal;
        // Reattach context menu defined on the ContentControl directly to the terminal control
        if (TerminalContentControl.ContextMenu != null && Terminal is FrameworkElement fe)
        {
            fe.ContextMenu = TerminalContentControl.ContextMenu;
            TerminalContentControl.ContextMenu = null; // prevent double visual parent issues
        }
        _server = server;
        _contentDialogService = contentDialogService;
        ThemeControl.ContentDialogService = contentDialogService;

        Terminal.InputLineSyntax = "";
        Terminal.CommandEntered += TerminalCommandEntered;
        Terminal.TerminalSizeChanged += TerminalSizeChanged;
        Terminal.TabCompletionRequested += TerminalTabCompletionRequested;

        // Initialize autocomplete services
        _pathCompletionService = new PathCompletionService();


        Loaded += ControlLoaded;
        SnippetControl.OnDeleteSnippet += SnippetControlOnDeleteSnippet;
        SnippetControl.OnEditSnippet += SnippetControlOnEditSnippet;
        SnippetControl.OnNewSnippet += SnippetControlOnNewSnippet;
        SnippetManager.OnSnippetInitialized += SnippetManagerOnSnippetInitialized;


        // Set up command bindings for copy/paste
        SetupCommandBindings();

        // Set up selection change monitoring
        SetupSelectionMonitoring();

        // Setup connection monitoring
        SetupConnectionMonitoring();

        // Subscribe to language changes
        LocalizationManager.LanguageChanged += OnLanguageChanged;
    }



    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Currently no dynamic text to update beyond localization of dialogs
    }

    private void SetupConnectionMonitoring()
    {
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
            if (_sshClient == null || !_sshClient.IsConnected)
            {
                await HandleConnectionTimeout(LocalizationManager.GetString("Connection_HealthCheckFailed") ?? "Connection lost");
            }
            else
            {
                // Optionally perform a lightweight keep-alive command (noop). Some servers ignore this so swallow exceptions.
                try
                {
                    // Sending a simple ignore packet isn't exposed; we can request a dummy exec if needed.
                    // For now rely on IsConnected.
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            await HandleConnectionTimeout(string.Format(LocalizationManager.GetString("Operation_ConnectionError") ?? "Connection error: {0}", ex.Message));
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

            // Store current working directory for restoration
            _lastKnownWorkingDirectory = _currentWorkingDirectory;

            Terminal.IsReadOnly = true;
            Progress.IsIndeterminate = true;
            Terminal.AddOutput(string.Format(LocalizationManager.GetString("Operation_ConnectionTimeoutDetected") ?? "Connection timeout detected: {0}", errorMessage));

            var reconnectionDialog = new Views.Dialog.ReconnectionDialog(_contentDialogService, _server, errorMessage);
            var result = await reconnectionDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await AttemptReconnection();
            }
            else
            {
                await CloseCurrentTab();
            }
        }
        catch (Exception ex)
        {
            Terminal.AddOutput(string.Format(LocalizationManager.GetString("Connection_HandleTimeoutError") ?? "Error handling timeout: {0}", ex.Message));
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
            Terminal.AddOutput(string.Format(LocalizationManager.GetString("Connection_Reconnecting") ?? "Reconnecting to {0}...", _server.Name));
            Progress.IsIndeterminate = true;
            Terminal.IsReadOnly = true;

            await ReconnectAsync();

            // Restore working directory if possible (send cd command)
            var restoreDir = !string.IsNullOrEmpty(_lastKnownWorkingDirectory) ? _lastKnownWorkingDirectory : "~";
            try
            {
                if (_sshClient != null && _sshClient.IsConnected && !string.IsNullOrWhiteSpace(restoreDir) && restoreDir != "~")
                {
                    _shellStream.WriteLine($"cd {restoreDir}");
                    _currentWorkingDirectory = restoreDir;
                }
            }
            catch { }

            Terminal.AddOutput(string.Format(LocalizationManager.GetString("Connection_SuccessfullyReconnected") ?? "Successfully reconnected to {0}", _server.Name));
            Terminal.IsReadOnly = false;
            Progress.IsIndeterminate = false;
            _connectionMonitorTimer?.Start();
        }
        catch (Exception ex)
        {
            Terminal.AddOutput(string.Format(LocalizationManager.GetString("Connection_ReconnectionFailed") ?? "Reconnection failed: {0}", ex.Message));
            Progress.IsIndeterminate = false;
            Terminal.IsReadOnly = true;

            // Ask again
            var errorDialog = new Views.Dialog.ReconnectionDialog(_contentDialogService, _server, ex.Message);
            var result = await errorDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await AttemptReconnection();
            }
            else
            {
                await CloseCurrentTab();
            }
        }
    }

    private async Task CloseCurrentTab()
    {
        try
        {
            var currentTab = SessionService.SelectedSession;
            if (currentTab != null && currentTab.Content == this)
            {
                await Task.Run(() => SessionService.RemoveSession(currentTab));
            }
        }
        catch (Exception ex)
        {
            Terminal.AddOutput(string.Format(LocalizationManager.GetString("Connection_ErrorClosingTab") ?? "Error closing tab: {0}", ex.Message));
        }
    }

    private async Task ReconnectAsync()
    {
        try
        {
            // Dispose old resources
            try
            {
                _shellStream?.Dispose();
                _sshClient?.Disconnect();
                _sshClient?.Dispose();
            }
            catch { }

            await ConnectToServer(_server);
        }
        catch
        {
            throw;
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
            await HandleConnectionTimeout(string.Format(LocalizationManager.GetString("Operation_Timeout") ?? "Timeout: {0}", ex.Message));
            //throw;
        }
        catch (SocketException ex)
        {
            await HandleConnectionTimeout(string.Format(LocalizationManager.GetString("Operation_NetworkError") ?? "Network error: {0}", ex.Message));
            //throw;
        }
        catch (Renci.SshNet.Common.SshConnectionException ex)
        {
            await HandleConnectionTimeout(string.Format(LocalizationManager.GetString("Operation_SSHConnectionError") ?? "SSH connection error: {0}", ex.Message));
            //throw;
        }
        catch (Exception ex) when (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase))
        {
            await HandleConnectionTimeout(string.Format(LocalizationManager.GetString("Operation_ConnectionError") ?? "Connection error: {0}", ex.Message));
            //throw;
        }
    }

    private async Task SnippetManagerOnSnippetInitialized()
    {
        if (!_isInitialized)
        {
            return;
        }
        var snippetList = SnippetManager.Snippets
       .Select(q => new SnippetViewModel(q, _contentDialogService))
       .ToList();

        foreach (var snip in snippetList)
        {
            snip.ExecuteSnippet += SnippetExecuteSnippet;
        }

        SnippetControl.Snippets = new ObservableCollection<SnippetViewModel>(snippetList);
        await Task.CompletedTask;
    }

    private async void ControlLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }
        var snippetList = SnippetManager.Snippets
            .Select(q => new SnippetViewModel(q, _contentDialogService))
            .ToList();

        foreach (var snip in snippetList)
        {
            snip.ExecuteSnippet += SnippetExecuteSnippet;
        }

        SnippetControl.Snippets = new ObservableCollection<SnippetViewModel>(snippetList);
        _isInitialized = true;
        if (_server == null)
        {
            Terminal.AddOutput(LocalizationManager.GetString("Terminal_ServerNotInitialized") ?? "Server is not initialized.");
            return;
        }
        Terminal.Focus();
        await ExecuteWithTimeoutDetection(async () => await ConnectToServer(_server));
    }

    private async Task ConnectToServer(ServerViewModel server)
    {
        Terminal.IsReadOnly = true;
        Progress.IsIndeterminate = true;
        if (server.UseKeyFile)
        {
            var privateKey = new PrivateKeyFile(server.PrivateKeyFilePath, server.KeyFilePassword);
            var keyFiles = new[] { privateKey };
            var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, new PrivateKeyAuthenticationMethod(server.Username, keyFiles));
            _sshClient = new SshClient(connectionInfo);
        }
        else
        {
            var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, new PasswordAuthenticationMethod(server.Username, server.Password));
            _sshClient = new SshClient(connectionInfo);
        }
        try
        {
            await _sshClient.ConnectAsync(CancellationToken.None);
            var cols = (uint)Math.Max(1, Terminal.Width / 8);
            var rows = (uint)Math.Max(1, Terminal.Height / 16);
            var pixelWidth = (uint)Terminal.Width;
            var pixelHeight = (uint)Terminal.Height;
            _shellStream = await Task.Run(() => _sshClient.CreateShellStream("vt100", 80, 24, 0, 0, 4096));

            _autoCompleteService = new SshAutoCompleteService(_sshClient, _pathCompletionService);

            StartReadLoop();

            // Start monitoring only after successful connection
            _connectionMonitorTimer?.Start();
        }
        catch (Exception ex)
        {
            var errorMessage = string.Format(
                LocalizationManager.GetString("Terminal_ConnectionFailed") ?? "Failed to connect to {0}: {1}",
                server.Name, ex.Message);
            Terminal.AddOutput(errorMessage);
            throw; // Ensure caller can trigger reconnection flow if needed
        }
        Terminal.IsReadOnly = false;
        Progress.IsIndeterminate = false;
    }

    private async Task StartReadLoop()
    {
        await Task.Run(async () =>
        {
            try
            {
                while (_sshClient is not null && _sshClient.IsConnected)
                {
                    string output = _shellStream.Read();
                    if (!string.IsNullOrEmpty(output))
                    {
                        await Task.Delay(200);
                        Application.Current.Dispatcher.Invoke(() =>
                        {

                            Terminal.AddOutput(output.ToString());
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // If read loop fails due to disconnection, trigger reconnection
                _ = Application.Current.Dispatcher.Invoke(async () =>
                {
                    await HandleConnectionTimeout(ex.Message);
                });
            }
        });
    }

    private void TerminalSizeChanged(ITerminal obj, Size newSize)
    {
        if (_shellStream != null && _sshClient != null && _sshClient.IsConnected)
        {
            double charWidth = 8.0;
            double charHeight = 16.0;
            UInt32 cols = (UInt32)Math.Max(10, (int)(newSize.Width / charWidth));
            UInt32 rows = (UInt32)Math.Max(2, (int)(newSize.Height / charHeight));
            var pixelWidth = (uint)newSize.Width;
            var pixelHeight = (uint)newSize.Height;

            try
            {
                var channelField = _shellStream.GetType()
                    .GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance);
                var channel = channelField?.GetValue(_shellStream);
                var method = channel?.GetType()
                    .GetMethod("SendWindowChangeRequest", BindingFlags.Public | BindingFlags.Instance);
                method?.Invoke(channel, new object[] { cols, rows, pixelWidth, pixelHeight });
            }
            catch (Exception ex)
            {
                var errorMessage = string.Format(
                    LocalizationManager.GetString("Terminal_ResizeFailed") ?? "Failed to resize terminal: {0}",
                    ex.Message);
                Terminal.AddOutput(errorMessage);
            }
        }
    }

    private void TerminalCommandEntered(ITerminal obj, string command)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                _shellStream.Write("\r");
            }
            else
            {
                _shellStream.WriteLine(command);
                // Track potential working directory changes (simple heuristic: cd commands)
                if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = command.Split(' ', 2);
                    if (parts.Length == 2)
                    {
                        _currentWorkingDirectory = parts[1].Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            var errorMessage = string.Format(
                LocalizationManager.GetString("Terminal_Error") ?? "Error: {0}",
                ex.Message);
            Terminal.AddOutput(errorMessage);
            // Attempt reconnection if likely connection related
            if (ex is TimeoutException || ex is SocketException || ex is Renci.SshNet.Common.SshConnectionException || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
            {
                _ = HandleConnectionTimeout(ex.Message);
            }
        }
    }

    private async void TerminalTabCompletionRequested(ITerminal obj, TabCompletionEventArgs args)
    {
        if (_autoCompleteService == null)
        {
            args.Handled = false;
            return;
        }

        try
        {
            args.WorkingDirectory = _currentWorkingDirectory;

            var result = await _autoCompleteService.GetAutoCompleteAsync(
                args.CurrentLine,
                args.CursorPosition,
                args.WorkingDirectory);

            if (result.HasSuggestions)
            {
                Terminal.ShowAutoCompleteResults(result);
                args.Handled = true;
            }
            else
            {
                Terminal.HideAutoComplete();
                args.Handled = false;
            }
        }
        catch (Exception ex)
        {
            var errorMessage = string.Format(
                LocalizationManager.GetString("Terminal_AutoCompleteError") ?? "AutoComplete error: {0}",
                ex.Message);
            System.Diagnostics.Debug.WriteLine(errorMessage);
            Terminal.HideAutoComplete();
            args.Handled = false;
        }
    }

    public void Dispose()
    {
        if (_clipboardTimer != null)
        {
            _clipboardTimer.Stop();
            _clipboardTimer = null;
        }
        LocalizationManager.LanguageChanged -= OnLanguageChanged;
        _connectionMonitorTimer?.Stop();
        _connectionMonitorTimer = null;
        if (_sshClient is not null)
        {
            try { _shellStream?.Dispose(); } catch { }
            try { _sshClient?.Disconnect(); } catch { }
            _sshClient?.Dispose();
        }
    }

    // === Existing selection & clipboard methods remain unchanged ===
    private void SetupCommandBindings()
    {
        // Create bindings once so we can add them to both this control and the terminal control.
        var copyBinding = new CommandBinding(ApplicationCommands.Copy, CopyCommand_Executed, CopyCommand_CanExecute);
        var pasteBinding = new CommandBinding(ApplicationCommands.Paste, PasteCommand_Executed, PasteCommand_CanExecute);
        var selectAllBinding = new CommandBinding(ApplicationCommands.SelectAll, SelectAllCommand_Executed, SelectAllCommand_CanExecute);

        CommandBindings.Add(copyBinding);
        CommandBindings.Add(pasteBinding);
        CommandBindings.Add(selectAllBinding);
        InputBindings.Add(new KeyBinding(ApplicationCommands.Copy, Key.C, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Paste, Key.V, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.SelectAll, Key.A, ModifierKeys.Control));

        // Also attach to the terminal control itself. When the terminal is moved into a fullscreen window,
        // it leaves this UserControl's visual tree, so routed commands would otherwise not find the bindings
        // (resulting in disabled context menu items). By placing bindings directly on the terminal UI element
        // they remain active regardless of reparenting.
        if (Terminal is UIElement uiTerminal)
        {
            uiTerminal.CommandBindings.Add(copyBinding);
            uiTerminal.CommandBindings.Add(pasteBinding);
            uiTerminal.CommandBindings.Add(selectAllBinding);
            uiTerminal.InputBindings.Add(new KeyBinding(ApplicationCommands.Copy, Key.C, ModifierKeys.Control));
            uiTerminal.InputBindings.Add(new KeyBinding(ApplicationCommands.Paste, Key.V, ModifierKeys.Control));
            uiTerminal.InputBindings.Add(new KeyBinding(ApplicationCommands.SelectAll, Key.A, ModifierKeys.Control));
        }
    }

    private void SetupSelectionMonitoring()
    {
        if (Terminal is GPTTerminalUserControl terminalControl)
        {
            terminalControl.MouseUp += (s, e) =>
            {
                Dispatcher.BeginInvoke(() => UpdateSelectionState(), DispatcherPriority.Background);
            };
            terminalControl.KeyUp += (s, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.Delete)
                {
                    Dispatcher.BeginInvoke(() => UpdateSelectionState(), DispatcherPriority.Background);
                }
            };
        }
        _clipboardTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clipboardTimer.Tick += (s, e) => UpdateClipboardState();
        _clipboardTimer.Start();
        UpdateSelectionState();
        UpdateClipboardState();
    }

    private void UpdateClipboardState()
    {
        try
        {
            bool hasClipboard = Clipboard.ContainsText();
            if (hasClipboard != _lastClipboardState)
            {
                _lastClipboardState = hasClipboard;
                _cachedClipboardState = hasClipboard;
                CommandManager.InvalidateRequerySuggested();
            }
        }
        catch (Exception)
        {
            _cachedClipboardState = false;
        }
    }

    private void UpdateSelectionState()
    {
        bool hasSelection = HasSelectedText();
        if (hasSelection != _lastSelectionState)
        {
            _lastSelectionState = hasSelection;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void CopyCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Terminal != null && _lastSelectionState;
    private void CopyCommand_Executed(object sender, ExecutedRoutedEventArgs e) { CopySelectedText(); UpdateSelectionState(); }
    private void PasteCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Terminal != null && !Terminal.IsReadOnly && _cachedClipboardState;
    private void PasteCommand_Executed(object sender, ExecutedRoutedEventArgs e) => PasteFromClipboard();
    private void SelectAllCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Terminal != null;
    private void SelectAllCommand_Executed(object sender, ExecutedRoutedEventArgs e) => Terminal?.SelectAll();
    private bool HasSelectedText() => Terminal is GPTTerminalUserControl terminalControl && terminalControl.HasSelection();
    private void CopySelectedText() { if (Terminal is GPTTerminalUserControl terminalControl) terminalControl.CopySelection(); }

    private void PasteFromClipboard()
    {
        if (Terminal != null && !Terminal.IsReadOnly && Clipboard.ContainsText())
        {
            if (Terminal is GPTTerminalUserControl terminalControl)
            {
                terminalControl.PasteFromClipboard();
            }
            else
            {
                string clipboardText = Clipboard.GetText();
                if (!string.IsNullOrEmpty(clipboardText))
                {
                    if (_shellStream != null && _sshClient != null && _sshClient.IsConnected)
                    {
                        _shellStream.Write(clipboardText);
                    }
                    else
                    {
                        Terminal.AddInput(clipboardText);
                    }
                }
            }
            Terminal.Focus();
        }
    }

    private async Task SnippetControlOnNewSnippet(SnippetUserControl obj)
    {
        if (_contentDialogService is not null)
        {
            var snippetViewModel = new SnippetViewModel(_contentDialogService);
            if (_contentDialogService is not null)
            {
                var snippetContentDialog = new SnippetContentDialog(_contentDialogService.GetDialogHost(), snippetViewModel);
                var contentDialogResult = await snippetContentDialog.ShowAsync();
                if (contentDialogResult == ContentDialogResult.Primary)
                {
                    await SnippetManager.Add(snippetViewModel.ToSnippet());
                    SnippetControl.AddSnippet(snippetViewModel);
                    snippetViewModel.ExecuteSnippet += SnippetExecuteSnippet;
                }
            }
        }
    }

    private async Task SnippetControlOnEditSnippet(SnippetUserControl obj, SnippetViewModel? snippet)
    {
        if (_contentDialogService is not null && snippet is not null)
        {
            var snippetViewModel = snippet;
            if (_contentDialogService is not null)
            {
                var snippetContentDialog = new SnippetContentDialog(_contentDialogService.GetDialogHost(), snippetViewModel);
                var contentDialogResult = await snippetContentDialog.ShowAsync();
                if (contentDialogResult == ContentDialogResult.Primary)
                {
                    await SnippetManager.Edit(snippetViewModel.ToSnippet());
                }
            }
        }
    }

    private async Task SnippetControlOnDeleteSnippet(SnippetUserControl obj, SnippetViewModel? snippet)
    {
        if (_contentDialogService is not null)
        {
            var message = string.Format(
                LocalizationManager.GetString("Snippet_DeleteConfirmMessage") ?? "Are you sure you want to delete {0}?",
                snippet?.Name);
            ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(
                    new SimpleContentDialogCreateOptions()
                    {
                        Title = LocalizationManager.GetString("Snippet_DeleteConfirmTitle") ?? "Delete this snippet?",
                        Content = message,
                        PrimaryButtonText = LocalizationManager.GetString("General_Delete") ?? "Delete",
                        CloseButtonText = LocalizationManager.GetString("General_Cancel") ?? "Cancel",
                    }
                );
            if (result == ContentDialogResult.Primary)
            {
                await SnippetManager.Remove(snippet?.ToSnippet());
                snippet.ExecuteSnippet -= SnippetExecuteSnippet;
                SnippetControl.RemoveSnippet(snippet);
            }
        }
    }

    private async Task SnippetExecuteSnippet(SnippetViewModel? snippet)
    {
        if (snippet?.Code is null)
        {
            return;
        }
        Terminal.AddInput(snippet.Code);
        Terminal.Focus();
        await Task.CompletedTask;
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        StackPanel menuButton = GetMenu();

        FullScreenButton.IsEnabled = false;
        TerminalContentControl.Content = null;
        _FullScreen = new FullScreenWindow(_contentDialogService, Terminal, _server.Name, menuButton);
        _FullScreen.Show();
        _FullScreen.Closed += (s, args) =>
        {
            _FullScreen.RootContentDialog.Content = null;
            TerminalContentControl.Content = Terminal;
            _FullScreen = null;
            FullScreenButton.IsEnabled = true;
        };
    }

    private StackPanel GetMenu()
    {
        var menuPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
        };
        var menuButton = new DropDownButton
        {
            Icon = new SymbolIcon
            {
                Symbol = SymbolRegular.Code20,
                Width = 20,
                Height = 20
            },
        };

        var ctx = new ContextMenu();
        ctx.MaxHeight = SystemParameters.PrimaryScreenHeight * 0.6;
        var snippets = SnippetControl.Snippets.ToList();
        if (snippets.Count > 15)
        {
            var systemSnippets = snippets.Where(s => s.IsSystemSnippet).ToList();
            var userSnippets = snippets.Where(s => !s.IsSystemSnippet).ToList();
            if (systemSnippets.Any())
            {
                var systemSubmenuLabel = string.Format(
                    LocalizationManager.GetString("Snippet_SystemSnippets") ?? "System Snippets ({0})",
                    systemSnippets.Count);
                var systemSubmenu = new MenuItem { Header = systemSubmenuLabel };
                if (systemSnippets.Count > 25)
                {
                    var groupedSnippets = systemSnippets
                        .GroupBy(s => s.GetCommandCategory())
                        .OrderBy(g => g.Key);
                    foreach (var group in groupedSnippets)
                    {
                        var categorySubmenu = new MenuItem { Header = $"{group.Key} ({group.Count()})" };
                        foreach (var snippet in group.Take(20))
                        {
                            var menu = new MenuItem { Header = snippet.Name };
                            menu.Click += (s, args) => { Terminal.AddInput(snippet.Code); };
                            categorySubmenu.Items.Add(menu);
                        }
                        systemSubmenu.Items.Add(categorySubmenu);
                    }
                }
                else
                {
                    foreach (var snippet in systemSnippets)
                    {
                        var menu = new MenuItem { Header = snippet.Name };
                        menu.Click += (s, args) => { Terminal.AddInput(snippet.Code); };
                        systemSubmenu.Items.Add(menu);
                    }
                }
                ctx.Items.Add(systemSubmenu);
            }
            if (userSnippets.Any())
            {
                var userSubmenuLabel = string.Format(
                    LocalizationManager.GetString("Snippet_UserSnippets") ?? "User Snippets ({0})",
                    userSnippets.Count);
                var userSubmenu = new MenuItem { Header = userSubmenuLabel };
                foreach (var snippet in userSnippets)
                {
                    var menu = new MenuItem { Header = snippet.Name };
                    menu.Click += (s, args) => { Terminal.AddInput(snippet.Code); };
                    userSubmenu.Items.Add(menu);
                }
                ctx.Items.Add(userSubmenu);
            }
            if (systemSnippets.Any() || userSnippets.Any())
            {
                ctx.Items.Add(new Separator());
                var quickAccess = snippets.Take(5);
                foreach (var snippet in quickAccess)
                {
                    var menu = new MenuItem
                    {
                        Header = $"⚡ {snippet.Name}",
                        FontWeight = FontWeights.SemiBold
                    };
                    menu.Click += (s, args) => { Terminal.AddInput(snippet.Code); };
                    ctx.Items.Add(menu);
                }
            }
        }
        else
        {
            foreach (var item in snippets)
            {
                var menu = new MenuItem { Header = item.Name };
                menu.Click += (s, args) => { Terminal.AddInput(item.Code); };
                ctx.Items.Add(menu);
            }
        }

        ctx.PlacementTarget = menuButton;
        menuButton.Flyout = ctx;
        menuPanel.Children.Add(menuButton);
        return menuPanel;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => Terminal?.SelectAll();

    private void SnippetToggleButtonChecked(object sender, RoutedEventArgs e)
    {
        SnippetControl.Visibility = Visibility.Visible;
        ThemeControl.Visibility = Visibility.Collapsed;
        if (ThemeToggleButton.IsChecked == true)
            ThemeToggleButton.IsChecked = false;
    }

    private void SnippetToggleButtonUnchecked(object sender, RoutedEventArgs e)
    {
        SnippetControl.Visibility = Visibility.Collapsed;
    }

    private void ThemeToggleButtonChecked(object sender, RoutedEventArgs e)
    {
        ThemeControl.Visibility = Visibility.Visible;
        SnippetControl.Visibility = Visibility.Collapsed;
        if (SnippetToggleButton.IsChecked == true)
            SnippetToggleButton.IsChecked = false;
        if (Terminal != null)
        {
            ThemeControl.Terminal = Terminal;
        }
    }

    private void ThemeToggleButtonUnchecked(object sender, RoutedEventArgs e)
    {
        ThemeControl.Visibility = Visibility.Collapsed;
    }
}