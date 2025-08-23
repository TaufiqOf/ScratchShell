using Humanizer;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Wpf.Ui.Controls;

namespace ScratchShell.UserControls.BrowserControl;

public class BrowserItem : INotifyPropertyChanged
{
    private string _name;
    private string _originalName;
    private bool _isInEditMode;
    private bool _isNewItem;
    private bool _isSelected;

    public string Name 
    { 
        get => _name;
        set 
        {
            _name = value;
            OnPropertyChanged();
        }
    }

    public string OriginalName 
    { 
        get => _originalName;
        set 
        {
            _originalName = value;
            OnPropertyChanged();
        }
    }

    public bool IsInEditMode 
    { 
        get => _isInEditMode;
        set 
        {
            _isInEditMode = value;
            OnPropertyChanged();
        }
    }

    public bool IsNewItem 
    { 
        get => _isNewItem;
        set 
        {
            _isNewItem = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected 
    { 
        get => _isSelected;
        set 
        {
            _isSelected = value;
            OnPropertyChanged();
        }
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void StartEdit()
    {
        OriginalName = Name;
        IsInEditMode = true;
    }

    public void CancelEdit()
    {
        Name = OriginalName;
        IsInEditMode = false;
        if (IsNewItem)
        {
            // This will be handled by the parent to remove the item
        }
    }

    public void CommitEdit()
    {
        IsInEditMode = false;
        IsNewItem = false;
    }
}