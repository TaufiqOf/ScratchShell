using System.Diagnostics;

namespace ScratchShell.Services.Terminal.Launcher;

public class WindowsTerminalLauncher : ITerminalLauncher
{
    public void Launch(string command, string? title = null, bool asAdmin = false)
    {
        var args = string.IsNullOrWhiteSpace(title)
            ? command
            : $"--title \"{title}\" {command}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "wt.exe",
            Arguments = args,
            UseShellExecute = true,
            CreateNoWindow = false
        };

        if (asAdmin)
        {
            startInfo.Verb = "runas";
        }

        Process.Start(startInfo);
    }
}
