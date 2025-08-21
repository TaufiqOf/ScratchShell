using ScratchShell.Models;

namespace ScratchShell.Services;

public class ConflictDetectedEventArgs : EventArgs
{
    public UserSettingsData? ServerSettings { get; set; }
    public UserSettingsData? LocalSettings { get; set; }
    public DateTime? ServerLastSynced { get; set; }
    public DateTime? ClientLastSynced { get; set; }
}