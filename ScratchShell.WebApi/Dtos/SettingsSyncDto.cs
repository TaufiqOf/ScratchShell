using System.ComponentModel.DataAnnotations;

namespace ScratchShell.WebApi.DTOs
{
    public class UserSettingsDto
    {
        public string? CurrentTheme { get; set; }
        public string? DefaultShellType { get; set; }
        public string? EncryptedServers { get; set; }
        public Dictionary<string, string> AdditionalSettings { get; set; } = new();
        public DateTime LastSyncedAt { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
    }

    public class SyncSettingsRequestDto
    {
        [Required]
        public UserSettingsDto Settings { get; set; } = new();
        public bool ForceOverwrite { get; set; } = false;
    }

    public class SyncSettingsResponseDto
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public UserSettingsDto? Settings { get; set; }
        public bool HasConflict { get; set; } = false;
        public DateTime? ServerLastSyncedAt { get; set; }
        public DateTime? ClientLastSyncedAt { get; set; }
    }

    public class GetSettingsResponseDto
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public UserSettingsDto? Settings { get; set; }
        public bool HasSettings { get; set; } = false;
    }

    public class DeleteSettingsResponseDto
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}