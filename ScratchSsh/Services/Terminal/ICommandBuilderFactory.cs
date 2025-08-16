namespace ScratchShell.Services.Terminal;

public interface ICommandBuilderFactory
{
    IShellCommandBuilder GetBuilder(string builderName);
}
