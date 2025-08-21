using Renci.SshNet;
using ScratchShell.Services;
using ScratchShell.UserControls.XtermTerminalControl;
using ScratchShell.View.Dialog;
using ScratchShell.ViewModels.Models;
using ScratchShell.Views.Windows;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using MenuItem = Wpf.Ui.Controls.MenuItem;

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

    public ITerminal Terminal { get; private set; }
    public SshUserControl(ServerViewModel server, IContentDialogService contentDialogService)
    {
        InitializeComponent();
        Terminal = new XTermTerminalUserControl();
        TerminalContentControl.Content = Terminal;
        _server = server;
        _contentDialogService = contentDialogService;
        Terminal.InputLineSyntax = "";
        Terminal.CommandEntered += TerminalCommandEntered;
        Terminal.TerminalSizeChanged += TerminalSizeChanged;
        Loaded += ControlLoaded;
        SnippetControl.OnDeleteSnippet += SnippetControlOnDeleteSnippet;
        SnippetControl.OnEditSnippet += SnippetControlOnEditSnippet;
        SnippetControl.OnNewSnippet += SnippetControlOnNewSnippet;
        SnippetManager.OnSnippetInitialized += SnippetManagerOnSnippetInitialized;
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
            Terminal.AddOutput("Server is not initialized.");
            return;
        }
        Terminal.AddOutput($"Connecting to {_server.Name} at {_server.Host}:{_server.Port} using {_server.ProtocolType} protocol.");
        // Here you would typically initiate the SSH connection using the server details.
        // For example, you might call a method to connect to the server.
        await ConnectToServer(_server);
    }

    private async Task ConnectToServer(ServerViewModel server)
    {
        Terminal.IsReadOnly = true;
        Progress.IsIndeterminate = true;
        if (server.UseKeyFile)
        {
            // Use key file authentication
            var privateKey = new PrivateKeyFile(server.PrivateKeyFilePath, server.KeyFilePassword);
            var keyFiles = new[] { privateKey };
            var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, new PrivateKeyAuthenticationMethod(server.Username, keyFiles));
            _sshClient = new SshClient(connectionInfo);
        }
        else
        {
            // Use password authentication
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
            _shellStream = _sshClient.CreateShellStream("vt100", 80, 24, 0, 0, 4096);

            Terminal.AddOutput($"Connected to {server.Name} at {server.Host}:{server.Port}.");
            StartReadLoop();
        }
        catch (Exception ex)
        {
            Terminal.AddOutput($"Failed to connect to {server.Name}: {ex.Message}");
            return;
        }
        Terminal.IsReadOnly = false;
        Progress.IsIndeterminate = false;
    }

    private async Task StartReadLoop()
    {
        await Task.Run(() =>
        {
            while (_sshClient is not null && _sshClient.IsConnected)
            {
                string output = _shellStream.Read();
                if (!string.IsNullOrEmpty(output))
                {
                    Application.Current.Dispatcher.Invoke(async () =>
                    {
                        Terminal.AddOutput(output.ToString());
                    });
                }
            }
        });
    }

    private void TerminalSizeChanged(ITerminal obj, Size newSize)
    {
        if (_shellStream != null && _sshClient != null && _sshClient.IsConnected)
        {
            // Estimate character cell size (adjust as needed for your font)
            double charWidth = 8.0;   // Typical width for Consolas 12pt
            double charHeight = 16.0; // Typical height for Consolas 12pt

            // Calculate columns and rows that fit in the new size
            UInt32 cols = (UInt32)Math.Max(10, (int)(newSize.Width / charWidth));
            UInt32 rows = (UInt32)Math.Max(2, (int)(newSize.Height / charHeight));
            var pixelWidth = (uint)newSize.Width;
            var pixelHeight = (uint)newSize.Height;

            try
            {
                // Use reflection to access the internal _channel field
                var channelField = _shellStream.GetType()
                    .GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance);
                var channel = channelField?.GetValue(_shellStream);

                // Call SendWindowChangeRequest on the channel
                var method = channel?.GetType()
                    .GetMethod("SendWindowChangeRequest", BindingFlags.Public | BindingFlags.Instance);
                method?.Invoke(channel, new object[] { cols, rows, pixelWidth, pixelHeight });
            }
            catch (Exception ex)
            {
                // Log or handle the error if reflection fails
                Terminal.AddOutput($"Failed to resize terminal: {ex.Message}");
            }
        }
    }

    private void TerminalCommandEntered(ITerminal obj, string command)
    {
        try
        {
            _shellStream.WriteLine(command);
        }
        catch (Exception ex)
        {
            Terminal.AddOutput("Error: " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (_sshClient is not null)
        {
            this._sshClient.Disconnect();
            this._sshClient.Dispose();
        }
    }

    private void SnippetToggleButtonChecked(object sender, RoutedEventArgs e)
    {
        SnippetControl.Visibility = Visibility.Visible;
    }

    private void SnippetToggleButtonUnchecked(object sender, RoutedEventArgs e)
    {
        SnippetControl.Visibility = Visibility.Collapsed;
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
            ContentDialogResult result = await _contentDialogService.ShowSimpleDialogAsync(
                    new SimpleContentDialogCreateOptions()
                    {
                        Title = "Delete this snippet?",
                        Content = $"Are you sure you want to delete {snippet?.Name}?",
                        PrimaryButtonText = "Delete",
                        CloseButtonText = "Cancel",
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
        await Task.CompletedTask;
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        StackPanel menuButton = GetMenu();

        FullScreenButton.IsEnabled = false;
        TerminalContentControl.Content = null;
        _FullScreen = new FullScreenWindow(Terminal, _server.Name, menuButton);
        _FullScreen.Show();
        _FullScreen.Closed += (s, args) =>
        {
            // Reinitialize the terminal when exiting full screen
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
        
        // Set maximum height for the context menu to enable scrolling
        ctx.MaxHeight = SystemParameters.PrimaryScreenHeight * 0.6; // 60% of screen height
        
        // Get the available snippets
        var snippets = SnippetControl.Snippets.ToList();
        
        // If there are many snippets, organize them intelligently
        if (snippets.Count > 15)
        {
            // Group snippets by categories for better organization
            var systemSnippets = snippets.Where(s => s.IsSystemSnippet).ToList();
            var userSnippets = snippets.Where(s => !s.IsSystemSnippet).ToList();
            
            // Add system snippets submenu if any exist
            if (systemSnippets.Any())
            {
                var systemSubmenu = new MenuItem { Header = $"System Snippets ({systemSnippets.Count})" };
                
                // If too many system snippets, create sub-categories
                if (systemSnippets.Count > 25)
                {
                    // Group by command type (based on first word of the command)
                    var groupedSnippets = systemSnippets
                        .GroupBy(s => s.GetCommandCategory())
                        .OrderBy(g => g.Key);
                        
                    foreach (var group in groupedSnippets)
                    {
                        var categorySubmenu = new MenuItem { Header = $"{group.Key} ({group.Count()})" };
                        foreach (var snippet in group.Take(20)) // Limit per category
                        {
                            var menu = new MenuItem { Header = snippet.Name };
                            menu.Click += (s, args) =>
                            {
                                Terminal.AddInput(snippet.Code);
                            };
                            categorySubmenu.Items.Add(menu);
                        }
                        systemSubmenu.Items.Add(categorySubmenu);
                    }
                }
                else
                {
                    // Add directly if not too many
                    foreach (var snippet in systemSnippets)
                    {
                        var menu = new MenuItem { Header = snippet.Name };
                        menu.Click += (s, args) =>
                        {
                            Terminal.AddInput(snippet.Code);
                        };
                        systemSubmenu.Items.Add(menu);
                    }
                }
                ctx.Items.Add(systemSubmenu);
            }
            
            // Add user snippets submenu if any exist  
            if (userSnippets.Any())
            {
                var userSubmenu = new MenuItem { Header = $"User Snippets ({userSnippets.Count})" };
                foreach (var snippet in userSnippets)
                {
                    var menu = new MenuItem { Header = snippet.Name };
                    menu.Click += (s, args) =>
                    {
                        Terminal.AddInput(snippet.Code);
                    };
                    userSubmenu.Items.Add(menu);
                }
                ctx.Items.Add(userSubmenu);
            }
            
            // Add separator and quick access to most recent/common snippets
            if (systemSnippets.Any() || userSnippets.Any())
            {
                ctx.Items.Add(new Separator());
                
                // Add a few quick access items (first 5 snippets)
                var quickAccess = snippets.Take(5);
                foreach (var snippet in quickAccess)
                {
                    var menu = new MenuItem 
                    { 
                        Header = $"⚡ {snippet.Name}",
                        FontWeight = FontWeights.SemiBold
                    };
                    menu.Click += (s, args) =>
                    {
                        Terminal.AddInput(snippet.Code);
                    };
                    ctx.Items.Add(menu);
                }
            }
        }
        else
        {
            // For smaller lists, add items directly
            foreach (var item in snippets)
            {
                var menu = new MenuItem { Header = item.Name };
                menu.Click += (s, args) =>
                {
                    // Execute the snippet code
                    Terminal.AddInput(item.Code);
                };
                ctx.Items.Add(menu);
            }
        }

        ctx.PlacementTarget = menuButton;
        menuButton.Flyout = ctx;
        menuPanel.Children.Add(menuButton);
        return menuPanel;
    }
    
    
}