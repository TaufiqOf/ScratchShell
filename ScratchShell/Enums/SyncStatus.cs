namespace ScratchShell.Enums
{
    public enum SyncStatus
    {
        Idle,
        Uploading,
        UploadCompleted,
        Downloading,
        DownloadCompleted,
        Error,
        ConflictDetected
    }
}