namespace ScratchShell.Services.Terminal;

public interface IShellCommandBuilder
{
    string BuildCommand(dynamic parameter);
}