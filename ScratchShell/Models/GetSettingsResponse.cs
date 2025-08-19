namespace ScratchShell.Models
{
    public class GetSettingsResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public UserSettingsData? Settings { get; set; }
        public bool HasSettings { get; set; }
    }
}