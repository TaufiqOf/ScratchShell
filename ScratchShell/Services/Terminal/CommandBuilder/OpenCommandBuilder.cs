using Microsoft.CSharp.RuntimeBinder;
using ScratchShell.Enums;
using ScratchShell.Services.Terminal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScratchShell.Services.Terminal.CommandBuilder
{
    public class OpenCommandBuilder : IShellCommandBuilder
    {
        public string BuildCommand(dynamic parameter)
        {
            try
            {
                ShellType shellType = parameter.ShellType;
                string path = parameter.Path ?? string.Empty;

                return shellType switch
                {
                    ShellType.CMD => $"cd /d \"{path}\"",
                    ShellType.PowerShell => $"cd \"{path}\"",
                    ShellType.WindowsTerminal => string.Empty, // wt handles path via launcher
                    _ => throw new ArgumentOutOfRangeException(nameof(shellType), "Unsupported shell type")
                };
            }
            catch (RuntimeBinderException)
            {
                throw new ArgumentException("Parameter must contain ShellType and Path properties.");
            }
        }
    }
}
