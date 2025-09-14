using ScratchShell.Enums;
using ScratchShell.Services;
using Wpf.Ui;

namespace ScratchShell.ViewModels.Models;

public partial class TabItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _header;

    private ServerViewModel server;

    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private IWorkspaceControl? _content;

    public delegate void RemovedHandler();
    public event RemovedHandler Removed;

    public string Icon { get; set; } = "XboxConsole24";

    public ServerViewModel Server
    {
        get
        {
            return server;
        }

        internal set
        {
            server = value;
            Header = $"{server.ProtocolType}: {server.Name}";
            if (server != null)
            {
                Content = server.ProtocolType switch
                {
                    ProtocolType.FTP => new UserControls.FtpUserControl(this),
                    ProtocolType.SSH => new UserControls.SshUserControl(this, ContentDialogService),
                    ProtocolType.SFTP => new UserControls.SftpUserControl(this, ContentDialogService),
                    _ => null
                };
            }
        }
    }

    public IContentDialogService ContentDialogService { get; internal set; }

    [RelayCommand]
    private void OnCloseTab(TabItemViewModel tab)
    {
        SessionService.RemoveSession(tab);
    }

    internal void Dispose()
    {
        Content?.Dispose();
    }

    internal void RemovedTab()
    {
        Removed.Invoke();
    }
}