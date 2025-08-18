using ScratchShell.Models;

namespace ScratchShell.Models
{
    public class SyncSettingsResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public UserSettingsData? Settings { get; set; }
        public bool HasConflict { get; set; }
        public DateTime? ServerLastSyncedAt { get; set; }
        public DateTime? ClientLastSyncedAt { get; set; }
    }
}