namespace ScratchShell.Services.Terminal;

public interface ITerminalLauncher
{
    void Launch(string command, string? title = null, bool asAdmin = false);
}