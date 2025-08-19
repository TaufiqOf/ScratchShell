namespace ScratchShell.Models
{
    public class SyncSettingsRequest
    {
        public UserSettingsData Settings { get; set; } = new();
        public bool ForceOverwrite { get; set; }
    }
}