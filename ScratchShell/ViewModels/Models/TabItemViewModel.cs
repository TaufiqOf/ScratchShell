using ScratchShell.Enums;
using ScratchShell.Services;
using ScratchShell.ViewModels.Models;
using ScratchShell.ViewModels.Pages;
using System.Windows.Controls;

namespace ScratchShell.ViewModels.Models;

public partial class TabItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _header;

    private ServerViewModel server;

    public Guid Id { get; set; } = Guid.NewGuid();
    [ObservableProperty]
    private IWorkspaceControl? _content;
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
                    ProtocolType.FTP => new UserControls.FtpUserControl(server),
                    ProtocolType.SSH => new UserControls.SshUserControl(server),
                    ProtocolType.SFTP => new UserControls.SftpUserControl(server),
                    _ => null
                };
            }
        }
    }

    [RelayCommand]
    private void OnCloseTab(TabItemViewModel tab)
    {
        SessionService.RemoveSession(tab);
    }

    internal void Dispose()
    {
        Content?.Dispose();
    }
}
