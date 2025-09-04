using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ScratchShell.Services;

namespace ScratchShell.UserControls.ThemeControl;

/// <summary>
/// Converter to check if a theme template is the selected one and return Visibility
/// </summary>
public class ThemeIsSelectedToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is ThemeTemplate current && values[1] is ThemeTemplate selected)
        {
            return ReferenceEquals(current, selected) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter to check if a theme template is the selected one
/// </summary>
public class ThemeIsSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is ThemeTemplate current && values[1] is ThemeTemplate selected)
        {
            return ReferenceEquals(current, selected);
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

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

public partial class ThemeUserControl : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty TerminalProperty = DependencyProperty.Register(
        nameof(Terminal), typeof(ITerminal), typeof(ThemeUserControl),
        new PropertyMetadata(null, OnTerminalChanged));

    public ITerminal Terminal
    {
        get => (ITerminal)GetValue(TerminalProperty);
        set => SetValue(TerminalProperty, value);
    }

    private readonly ObservableCollection<ThemeTemplate> _themeTemplates = new();
    private ThemeTemplate? _selectedTemplate;

    public ObservableCollection<ThemeTemplate> ThemeTemplates => _themeTemplates;

    public ThemeTemplate? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            _selectedTemplate = value;
            OnPropertyChanged();
            UpdateButtonStates();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ThemeUserControl()
    {
        InitializeComponent();
        DataContext = this;
        InitializeDefaultThemes();
        Loaded += ThemeUserControl_Loaded;
    }

    private void ThemeUserControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateButtonStates();
    }

    private void InitializeDefaultThemes()
    {
        // Add default theme templates
        _themeTemplates.Add(new ThemeTemplate
        {
            Name = "Default Dark",
            Description = "Standard dark terminal theme",
            IsDefault = true,
            Theme = new TerminalTheme
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 16.0,
                Foreground = Brushes.LightGray,
                Background = Brushes.Black,
                SelectionColor = Color.FromArgb(80, 0, 120, 255),
                CursorColor = Brushes.LightGray,
                CopySelectionColor = Color.FromArgb(180, 144, 238, 144)
            }
        });

        _themeTemplates.Add(new ThemeTemplate
        {
            Name = "One Dark",
            Description = "Popular Atom One Dark theme",
            IsDefault = true,
            Theme = new TerminalTheme
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 16.0,
                Foreground = new SolidColorBrush(Color.FromRgb(248, 248, 242)),
                Background = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
                SelectionColor = Color.FromArgb(80, 98, 114, 164),
                CursorColor = new SolidColorBrush(Color.FromRgb(248, 248, 242)),
                CopySelectionColor = Color.FromArgb(180, 87, 227, 137)
            }
        });

        _themeTemplates.Add(new ThemeTemplate
        {
            Name = "Light Theme",
            Description = "Clean light terminal theme",
            IsDefault = true,
            Theme = new TerminalTheme
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 16.0,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                SelectionColor = Color.FromArgb(80, 0, 120, 215),
                CursorColor = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                CopySelectionColor = Color.FromArgb(180, 0, 150, 0)
            }
        });

        _themeTemplates.Add(new ThemeTemplate
        {
            Name = "Monokai",
            Description = "Classic Monokai color scheme",
            IsDefault = true,
            Theme = new TerminalTheme
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 16.0,
                Foreground = new SolidColorBrush(Color.FromRgb(248, 248, 242)),
                Background = new SolidColorBrush(Color.FromRgb(39, 40, 34)),
                SelectionColor = Color.FromArgb(80, 73, 72, 62),
                CursorColor = new SolidColorBrush(Color.FromRgb(249, 38, 114)),
                CopySelectionColor = Color.FromArgb(180, 102, 217, 239)
            }
        });
    }

    private static void OnTerminalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ThemeUserControl ctrl)
        {
            ctrl.UpdateUIFromTheme();
        }
    }

    private void UpdateUIFromTheme()
    {
        // Update UI when terminal changes
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void UpdateButtonStates()
    {
        // Enable/disable buttons based on selection
        var hasSelection = SelectedTemplate != null;
        var canEdit = hasSelection && !SelectedTemplate?.IsDefault == true;
        var canDelete = hasSelection && !SelectedTemplate?.IsDefault == true;

        // Update button states (will be handled in XAML binding)
    }

    private void NewTheme_Click(object sender, RoutedEventArgs e)
    {
        CreateNewTheme();
    }

    private void EditTheme_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate != null)
        {
            EditTheme(SelectedTemplate);
        }
    }

    private void DeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate != null && !SelectedTemplate.IsDefault)
        {
            DeleteTheme(SelectedTemplate);
        }
    }

    private void ApplyTheme_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate != null)
        {
            ApplyTheme(SelectedTemplate);
        }
    }

    private void ApplyThemeItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ThemeTemplate template)
        {
            ApplyTheme(template);
        }
    }

    private void PreviewControl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ThemeTemplate template)
        {
            SelectedTemplate = template;
        }
    }

    private void CreateNewTheme()
    {
        var newTemplate = new ThemeTemplate
        {
            Name = "New Theme",
            Description = "Custom theme",
            Theme = Terminal?.Theme != null ? CloneTerminalTheme(Terminal.Theme) : CreateDefaultTheme()
        };

        ShowThemeEditor(newTemplate, isNew: true);
    }

    private void EditTheme(ThemeTemplate template)
    {
        if (template.IsDefault)
        {
            // Create a copy for editing
            var editTemplate = template.Clone();
            editTemplate.Name = $"{template.Name} (Copy)";
            editTemplate.IsDefault = false;
            ShowThemeEditor(editTemplate, isNew: true);
        }
        else
        {
            ShowThemeEditor(template, isNew: false);
        }
    }

    private void DeleteTheme(ThemeTemplate template)
    {
        if (template.IsDefault) return;

        _themeTemplates.Remove(template);
        if (SelectedTemplate == template)
        {
            SelectedTemplate = _themeTemplates.FirstOrDefault();
        }
    }

    private void ApplyTheme(ThemeTemplate template)
    {
        if (Terminal?.Theme != null)
        {
            // Copy theme properties to terminal
            Terminal.Theme.FontFamily = template.Theme.FontFamily;
            Terminal.Theme.FontSize = template.Theme.FontSize;
            Terminal.Theme.Foreground = template.Theme.Foreground;
            Terminal.Theme.Background = template.Theme.Background;
            Terminal.Theme.SelectionColor = template.Theme.SelectionColor;
            Terminal.Theme.CursorColor = template.Theme.CursorColor;
            Terminal.Theme.CopySelectionColor = template.Theme.CopySelectionColor;

            Terminal.RefreshTheme();
        }
    }

    private void ShowThemeEditor(ThemeTemplate template, bool isNew)
    {
        // Create and show theme editor dialog
        var editorWindow = new Window
        {
            Title = isNew ? "New Theme" : "Edit Theme",
            Width = 800,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.CanResize
        };

        var editorControl = new EditThemeUserControl
        {
            Terminal = Terminal
        };

        // Set the preview theme to the template's theme
        editorControl.PreviewTheme = template.Theme;

        var contentControl = new ContentControl
        {
            Content = editorControl
        };

        editorWindow.Content = contentControl;

        // Handle theme application
        editorControl.Loaded += (s, e) =>
        {
            // Additional setup if needed
        };

        var result = editorWindow.ShowDialog();
        if (result == true)
        {
            template.ModifiedDate = DateTime.Now;
            
            if (isNew)
            {
                _themeTemplates.Add(template);
            }
            
            SelectedTemplate = template;
        }
    }

    private static TerminalTheme CreateDefaultTheme()
    {
        return new TerminalTheme
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 16.0,
            Foreground = Brushes.LightGray,
            Background = Brushes.Black,
            SelectionColor = Color.FromArgb(80, 0, 120, 255),
            CursorColor = Brushes.LightGray,
            CopySelectionColor = Color.FromArgb(180, 144, 238, 144)
        };
    }

    private static TerminalTheme CloneTerminalTheme(TerminalTheme original)
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