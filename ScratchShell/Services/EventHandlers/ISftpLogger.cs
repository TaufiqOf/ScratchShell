using System;

namespace ScratchShell.Services.EventHandlers
{
    /// <summary>
    /// Interface for SFTP logging functionality
    /// </summary>
    public interface ISftpLogger
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? exception = null);
        void LogDebug(string message);
    }
}