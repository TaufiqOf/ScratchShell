using System.Diagnostics;

namespace ScratchShell.Services.Terminal.Launcher;

public class PowerShellLauncher : ITerminalLauncher
{
    public void Launch(string command, string? title = null, bool asAdmin = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoExit -Command \"{command}\"",
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