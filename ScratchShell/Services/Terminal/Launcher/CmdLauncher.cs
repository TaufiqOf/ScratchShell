using System.Diagnostics;

namespace ScratchShell.Services.Terminal.Launcher;

public class CmdLauncher : ITerminalLauncher
{
    public void Launch(string command, string? title = null, bool asAdmin = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/k {command}",
            UseShellExecute = true,
            CreateNoWindow = false,
            Verb = asAdmin ? "runas" : null
        };
        Process.Start(startInfo);
    }
}