using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using ScratchShell.Services;
using ScratchShell.Services.EventHandlers;
using ScratchShell.ViewModels.Models;

namespace ScratchShell.Services.Connection
{
    /// <summary>
    /// Manages SFTP client connections and provides file operation services
    /// </summary>
    public class SftpConnectionManager : ISftpConnectionManager
    {
        private readonly ISftpLogger _logger;
        private SftpClient? _sftpClient;
        private ISftpFileOperationService? _fileOperationService;
        private ServerViewModel? _lastConnectedServer;

        public SftpClient? Client => _sftpClient;
        public ISftpFileOperationService? FileOperationService => _fileOperationService;
        public bool IsConnected => _sftpClient?.IsConnected ?? false;

        public SftpConnectionManager(ISftpLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ConnectAsync(ServerViewModel server)
        {
            try
            {
                _logger.LogInfo($"Attempting to connect to {server.Name} ({server.Host}:{server.Port})");

                _sftpClient = CreateSftpClient(server);
                await _sftpClient.ConnectAsync(CancellationToken.None);

                _fileOperationService = new SftpFileOperationService(_sftpClient);
                SetupFileOperationServiceEvents();

                _lastConnectedServer = server; // Store for reconnection

                _logger.LogInfo($"Successfully connected to {server.Name} at {server.Host}:{server.Port}");
                _logger.LogInfo($"Working directory: {_sftpClient.WorkingDirectory}");
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                _logger.LogError($"Network connection failed to {server.Name}", ex);
                throw;
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                _logger.LogError($"SSH connection failed to {server.Name}", ex);
                throw;
            }
            catch (Renci.SshNet.Common.SshAuthenticationException ex)
            {
                _logger.LogError($"Authentication failed for {server.Name}", ex);
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError($"Access denied to {server.Name}", ex);
                throw;
            }
            catch (TimeoutException ex)
            {
                _logger.LogError($"Connection timeout to {server.Name}", ex);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Connection failed to {server.Name}", ex);
                throw;
            }
        }

        public async Task ReconnectAsync()
        {
            if (_lastConnectedServer == null)
            {
                throw new InvalidOperationException("No previous connection information available for reconnection");
            }

            _logger.LogInfo($"Attempting to reconnect to {_lastConnectedServer.Name}");
            
            // Dispose of existing connection
            await DisconnectAsync();
            
            // Create new connection
            await ConnectAsync(_lastConnectedServer);
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_sftpClient?.IsConnected == true)
                {
                    _logger.LogInfo("Disconnecting from server");
                    _sftpClient.Disconnect();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error disconnecting SFTP client", ex);
            }
        }

        /// <summary>
        /// Checks if the connection is still alive and working
        /// </summary>
        public bool IsConnectionAlive()
        {
            try
            {
                if (_sftpClient == null || !_sftpClient.IsConnected)
                    return false;

                // Try to perform a simple operation to verify connection
                _ = _sftpClient.WorkingDirectory;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private SftpClient CreateSftpClient(ServerViewModel server)
        {
            try
            {
                ConnectionInfo connectionInfo;

                if (server.UseKeyFile)
                {
                    _logger.LogInfo($"Using key file authentication: {server.PrivateKeyFilePath}");

                    if (string.IsNullOrEmpty(server.PrivateKeyFilePath) || !File.Exists(server.PrivateKeyFilePath))
                    {
                        throw new FileNotFoundException($"Private key file not found: {server.PrivateKeyFilePath}");
                    }

                    var privateKey = new PrivateKeyFile(server.PrivateKeyFilePath, server.KeyFilePassword);
                    var keyFiles = new[] { privateKey };
                    connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username,
                        new PrivateKeyAuthenticationMethod(server.Username, keyFiles));
                }
                else
                {
                    _logger.LogInfo($"Using password authentication for user: {server.Username}");
                    connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username,
                        new PasswordAuthenticationMethod(server.Username, server.Password));
                }

                return new SftpClient(connectionInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error creating SFTP client", ex);
                throw;
            }
        }

        private void SetupFileOperationServiceEvents()
        {
            try
            {
                if (_fileOperationService == null) return;

                _fileOperationService.LogRequested += _logger.LogInfo;
                _fileOperationService.ProgressChanged += (show, message, current, total) =>
                {
                    try
                    {
                        // Progress handling will be managed by the file operation handler
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error updating progress", ex);
                    }
                };
                _fileOperationService.ClipboardStateChanged += () =>
                {
                    try
                    {
                        // Clipboard state changes will be handled by the file operation handler
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error handling clipboard state change", ex);
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("Error setting up file operation service events", ex);
            }
        }

        public void Dispose()
        {
            try
            {
                DisconnectAsync().Wait(5000); // Wait up to 5 seconds for disconnection
                
                if (_fileOperationService != null)
                {
                    _fileOperationService.LogRequested -= _logger.LogInfo;
                    _fileOperationService = null;
                }

                _sftpClient?.Dispose();
                _sftpClient = null;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error disposing connection manager", ex);
            }
        }
    }
}