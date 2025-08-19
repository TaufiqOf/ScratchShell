using ScratchShell.Enums;
using ScratchShell.Services.Terminal;
using ScratchShell.Services.Terminal.Launcher;

namespace ScratchShell.Services;

internal static class TerminalService
{
    private static readonly ICommandBuilderFactory _commandFactory;
    private static readonly IDictionary<ShellType, ITerminalLauncher> _launchers;

    static TerminalService()
    {
        _commandFactory = new CommandBuilderFactory();
        _launchers = new Dictionary<ShellType, ITerminalLauncher>
        {
            { ShellType.CMD, new CmdLauncher() },
            { ShellType.PowerShell, new PowerShellLauncher() },
            { ShellType.WindowsTerminal, new WindowsTerminalLauncher() }
        };
    }

    internal static void Launch(string builderName, dynamic parameter, ShellType shellType, bool asAdmin, string title = "")
    {
        var launcher = _launchers[shellType];

        var builder = _commandFactory.GetBuilder(builderName);
        var command = builder.BuildCommand(parameter);

        launcher.Launch(command, title, asAdmin);
    }
}