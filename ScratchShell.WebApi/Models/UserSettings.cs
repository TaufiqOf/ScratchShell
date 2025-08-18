using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScratchShell.WebApi.Models
{
    public class UserSettings
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string UserId { get; set; } = string.Empty;
        
        public string? CurrentTheme { get; set; }
        public string? DefaultShellType { get; set; }
        
        [Column(TypeName = "text")]
        public string? EncryptedServers { get; set; }
        
        [Column(TypeName = "jsonb")]
        public string? AdditionalSettingsJson { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
        
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        
        // Navigation property
        public virtual User User { get; set; } = null!;
        
        // Helper property for additional settings
        [NotMapped]
        public Dictionary<string, string> AdditionalSettings
        {
            get
            {
                if (string.IsNullOrEmpty(AdditionalSettingsJson))
                    return new Dictionary<string, string>();
                
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(AdditionalSettingsJson) 
                           ?? new Dictionary<string, string>();
                }
                catch
                {
                    return new Dictionary<string, string>();
                }
            }
            set
            {
                AdditionalSettingsJson = System.Text.Json.JsonSerializer.Serialize(value);
            }
        }
    }
}