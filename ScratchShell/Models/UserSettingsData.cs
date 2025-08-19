namespace ScratchShell.Models
{
    public class UserSettingsData
    {
        public string? CurrentTheme { get; set; }
        public string? DefaultShellType { get; set; }
        public string? EncryptedServers { get; set; }
        public Dictionary<string, string> AdditionalSettings { get; set; } = new();
        public DateTime LastSyncedAt { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Snippets { get; set; } = string.Empty;
    }
}