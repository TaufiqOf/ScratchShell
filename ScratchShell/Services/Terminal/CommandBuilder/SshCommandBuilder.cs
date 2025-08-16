namespace ScratchShell.Services.Terminal.CommandBuilder;

public class SshCommandBuilder : IShellCommandBuilder
{
    public string BuildCommand(dynamic parameter)
    {
        var cmd = $"ssh {parameter.Username}@{parameter.Host} -p {parameter.Port}";

        if (parameter.UseKeyFile && !string.IsNullOrWhiteSpace(parameter.PrivateKeyFilePath))
        {
            cmd += $" -i \"{parameter.PrivateKeyFilePath}\"";

            if (!string.IsNullOrWhiteSpace(parameter.KeyFilePassword))
            {
                cmd += " -o PasswordAuthentication=yes -o PubkeyAuthentication=no";
            }
        }
        else if (!string.IsNullOrWhiteSpace(parameter.Password))
        {
            cmd += " -o PasswordAuthentication=yes";
        }

        return cmd;
    }
}
