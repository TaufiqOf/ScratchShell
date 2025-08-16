using Humanizer;

namespace ScratchShell.UserControls.BrowserControl;

public class BrowserItem
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public bool IsFolder { get; set; }
    public long Size { get; set; }

    public string SizeFormatted => IsFolder ? "N/A" :Size.Bytes().ToString();
    public DateTime LastUpdated { get; set; }

    public string DisplayType => IsFolder ? "Folder" : System.IO.Path.GetExtension(Name)?? "File" ;
}
