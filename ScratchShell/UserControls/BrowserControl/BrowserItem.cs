using Humanizer;
using Wpf.Ui.Controls;

namespace ScratchShell.UserControls.BrowserControl;

public class BrowserItem
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public bool IsFolder { get; set; }
    public long Size { get; set; }

    public string SizeFormatted => IsFolder ? "N/A" : Size.Bytes().ToString();
    public DateTime LastUpdated { get; set; }

    public string DisplayType => IsFolder ? "Folder" : System.IO.Path.GetExtension(Name) ?? "File";
    
    // Add Icon property for Windows Explorer-like appearance
    public SymbolRegular Icon => GetIcon();
    
    private SymbolRegular GetIcon()
    {
        if (Name == "..")
            return SymbolRegular.ArrowUp24;
            
        if (IsFolder)
            return SymbolRegular.Folder24;
            
        // Return icons based on file extension
        var extension = System.IO.Path.GetExtension(Name)?.ToLowerInvariant();
        return extension switch
        {
            ".txt" or ".log" or ".md" or ".readme" => SymbolRegular.Document24,
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".ico" => SymbolRegular.Image24,
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => SymbolRegular.Video24,
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => SymbolRegular.MusicNote224,
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => SymbolRegular.FolderZip24,
            ".pdf" => SymbolRegular.DocumentPdf24,
            ".exe" or ".msi" or ".deb" or ".rpm" => SymbolRegular.Apps24,
            ".cs" or ".vb" or ".cpp" or ".h" or ".java" or ".py" or ".js" or ".html" or ".css" => SymbolRegular.Code24,
            ".xml" or ".json" or ".yaml" or ".yml" => SymbolRegular.DocumentData24,
            ".sql" => SymbolRegular.Database24,
            _ => SymbolRegular.Document24
        };
    }
}