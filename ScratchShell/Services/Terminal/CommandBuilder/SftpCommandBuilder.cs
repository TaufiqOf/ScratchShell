namespace ScratchShell.Services.Terminal.CommandBuilder;

public class SftpCommandBuilder : IShellCommandBuilder
{
    public string BuildCommand(dynamic parameter)
    {
        var cmd = $"sftp -P {parameter.Port}";

        if (parameter.UseKeyFile && !string.IsNullOrWhiteSpace(parameter.PrivateKeyFilePath))
        {
            cmd += $" -i \"{parameter.PrivateKeyFilePath}\"";

            if (!string.IsNullOrWhiteSpace(parameter.KeyFilePassword))
            {
                // Note: sftp does not natively support keyfile passwords via CLI
                // This might require an SSH agent or expect script
                cmd += " -o PasswordAuthentication=yes -o PubkeyAuthentication=no";
            }
        }
        else if (!string.IsNullOrWhiteSpace(parameter.Password))
        {
            // Note: sftp does not support passing password in the command.
            // You would need sshpass or expect script to automate this.
            cmd = $"sshpass -p \"{parameter.Password}\" " + cmd;
            cmd += " -o PasswordAuthentication=yes";
        }

        cmd += $" {parameter.Username}@{parameter.Host}";

        return cmd;
    }
}

