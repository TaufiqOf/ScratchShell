using ScratchShell.Services.Terminal.CommandBuilder;

namespace ScratchShell.Services.Terminal
{
    public class CommandBuilderFactory : ICommandBuilderFactory
    {
        public IShellCommandBuilder GetBuilder(string builderName)
        {
            return builderName switch
            {
                Constants.TerminalConstant.Builder.SSH => new SshCommandBuilder(),
                Constants.TerminalConstant.Builder.FTP => new FtpCommandBuilder(),
                Constants.TerminalConstant.Builder.SFTP => new SftpCommandBuilder(),
                Constants.TerminalConstant.Builder.Open => new OpenCommandBuilder(),
                _ => throw new NotSupportedException($"Builder {builderName} is not supported.")
            };
        }
    }
}
