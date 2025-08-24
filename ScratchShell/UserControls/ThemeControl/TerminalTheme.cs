using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ScratchShell.UserControls.ThemeControl;

public class TerminalTheme : INotifyPropertyChanged
{
    private FontFamily _fontFamily = new FontFamily("Consolas");
    private double _fontSize = 16.0;
    private Brush _foreground = Brushes.LightGray;
    private Brush _background = Brushes.Black;
    private Color _selectionColor = Color.FromArgb(80, 0, 120, 255);
    private Brush _cursorColor = Brushes.LightGray;
    private Color _copySelectionColor = Color.FromArgb(180, 144, 238, 144);

    public FontFamily FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    public Brush Foreground
    {
        get => _foreground;
        set => SetProperty(ref _foreground, value);
    }

    public Brush Background
    {
        get => _background;
        set => SetProperty(ref _background, value);
    }

    public Color SelectionColor
    {
        get => _selectionColor;
        set => SetProperty(ref _selectionColor, value);
    }

    public Brush CursorColor
    {
        get => _cursorColor;
        set => SetProperty(ref _cursorColor, value);
    }

    public Color CopySelectionColor
    {
        get => _copySelectionColor;
        set => SetProperty(ref _copySelectionColor, value);
    }

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
}