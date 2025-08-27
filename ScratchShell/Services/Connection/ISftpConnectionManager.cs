using System;
using System.Threading.Tasks;
using Renci.SshNet;
using ScratchShell.Services;
using ScratchShell.ViewModels.Models;

namespace ScratchShell.Services.Connection
{
    /// <summary>
    /// Interface for managing SFTP connections
    /// </summary>
    public interface ISftpConnectionManager : IDisposable
    {
        SftpClient? Client { get; }
        ISftpFileOperationService? FileOperationService { get; }
        bool IsConnected { get; }
        
        Task ConnectAsync(ServerViewModel server);
        Task ReconnectAsync();
        Task DisconnectAsync();
        bool IsConnectionAlive();
    }
}