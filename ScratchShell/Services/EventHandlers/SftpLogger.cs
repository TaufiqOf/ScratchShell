using System;

namespace ScratchShell.Services.EventHandlers
{
    /// <summary>
    /// Implementation of SFTP logger that writes to terminal
    /// </summary>
    public class SftpLogger : ISftpLogger
    {
        private readonly Action<string> _outputAction;

        public SftpLogger(Action<string> outputAction)
        {
            _outputAction = outputAction ?? throw new ArgumentNullException(nameof(outputAction));
        }

        public void LogInfo(string message)
        {
            LogWithTimestamp($"?? {message}");
        }

        public void LogWarning(string message)
        {
            LogWithTimestamp($"?? {message}");
        }

        public void LogError(string message, Exception? exception = null)
        {
            var errorMessage = exception != null 
                ? $"? {message}: {exception.GetType().Name} - {exception.Message}"
                : $"? {message}";
            
            LogWithTimestamp(errorMessage);
            
            if (exception?.StackTrace != null)
            {
                LogWithTimestamp($"?? Stack trace: {exception.StackTrace}");
            }
        }

        public void LogDebug(string message)
        {
            LogWithTimestamp($"?? {message}");
        }

        private void LogWithTimestamp(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var logMessage = $"[{timestamp}] {message}\n";
                _outputAction(logMessage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log error: {ex.Message}. Original message: {message}");
            }
        }
    }
}