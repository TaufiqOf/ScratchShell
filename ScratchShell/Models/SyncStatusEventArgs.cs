using ScratchShell.Enums;

namespace ScratchShell.Services;

public class SyncStatusEventArgs : EventArgs
{
    public SyncStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}