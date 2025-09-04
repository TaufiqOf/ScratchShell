using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ScratchShell.UserControls.ThemeControl;

/// <summary>
/// Theme template model for managing saved themes
/// </summary>
public class ThemeTemplate : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private TerminalTheme _theme = new();
    private bool _isDefault;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public TerminalTheme Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    public bool IsDefault
    {
        get => _isDefault;
        set => SetProperty(ref _isDefault, value);
    }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime ModifiedDate { get; set; } = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Creates a deep copy of the theme template
    /// </summary>
    public ThemeTemplate Clone()
    {
        return new ThemeTemplate
        {
            Name = Name,
            Description = Description,
            Theme = CloneTheme(Theme),
            IsDefault = IsDefault,
            CreatedDate = CreatedDate,
            ModifiedDate = DateTime.Now
        };
    }

    private static TerminalTheme CloneTheme(TerminalTheme original)
    {
        return new TerminalTheme
        {
            FontFamily = original.FontFamily,
            FontSize = original.FontSize,
            Foreground = original.Foreground,
            Background = original.Background,
            SelectionColor = original.SelectionColor,
            CursorColor = original.CursorColor,
            CopySelectionColor = original.CopySelectionColor
        };
    }
}
