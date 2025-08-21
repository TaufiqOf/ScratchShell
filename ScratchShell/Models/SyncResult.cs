namespace ScratchShell.Models;

public class SyncResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool HasConflict { get; set; }
    public UserSettingsData? ServerSettings { get; set; }
    public bool RequiresPasswordReentry { get; set; }
}