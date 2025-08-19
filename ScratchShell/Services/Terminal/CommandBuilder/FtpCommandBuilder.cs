namespace ScratchShell.Services.Terminal.CommandBuilder;

public class FtpCommandBuilder : IShellCommandBuilder
{
    public string BuildCommand(dynamic parameter)
    {
        // FTP usually uses just "ftp host"
        return $"ftp {parameter.Host}";
    }
}