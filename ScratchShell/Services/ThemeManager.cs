using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media;
using ScratchShell.Properties;
using ScratchShell.UserControls;
using ScratchShell.UserControls.ThemeControl;

namespace ScratchShell.Services;

/// <summary>
/// Service for managing terminal themes, saving them to application settings, and providing them to the application
/// </summary>
public class ThemeManager : INotifyPropertyChanged
{
    private static ThemeManager? _instance;
    private readonly ObservableCollection<ThemeTemplate> _themeTemplates = new();
    private ThemeTemplate? _currentTheme;
    private readonly UserSettingsService _userSettingsService;

    public static ThemeManager Instance => _instance ??= new ThemeManager();

    public ObservableCollection<ThemeTemplate> ThemeTemplates => _themeTemplates;

    public ThemeTemplate? CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                OnPropertyChanged();
                CurrentThemeChanged?.Invoke(value);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<ThemeTemplate?>? CurrentThemeChanged;

    private ThemeManager()
    {
        _userSettingsService = new UserSettingsService();
        InitializeDefaultThemes();
        LoadSavedThemes();
        LoadCurrentTheme();
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

        // Set default theme
        if (!_themeTemplates.Any(t => t.IsDefault))
        {
            _themeTemplates.First().IsDefault = true;
        }
        CurrentTheme = _themeTemplates.FirstOrDefault(t => t.IsDefault);
    }

    private void LoadSavedThemes()
    {
        try
        {
            var savedThemesJson = Settings.Default.SavedTerminalThemes;
            if (!string.IsNullOrEmpty(savedThemesJson))
            {
                var savedThemes = JsonSerializer.Deserialize<List<ThemeTemplateDto>>(savedThemesJson);
                if (savedThemes != null)
                {
                    foreach (var themeDto in savedThemes)
                    {
                        var theme = ConvertFromDto(themeDto);
                        if (theme != null && !theme.IsDefault)
                        {
                            _themeTemplates.Add(theme);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading saved themes: {ex.Message}");
        }
    }

    private void LoadCurrentTheme()
    {
        try
        {
            var currentThemeName = Settings.Default.CurrentTerminalTheme;
            if (!string.IsNullOrEmpty(currentThemeName))
            {
                var theme = _themeTemplates.FirstOrDefault(t => t.Name == currentThemeName);
                if (theme != null)
                {
                    CurrentTheme = theme;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading current theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds or updates a theme template
    /// </summary>
    public void SaveTheme(ThemeTemplate template)
    {
        if (template == null) return;

        try
        {
            // Check if theme already exists
            var existingTheme = _themeTemplates.FirstOrDefault(t => t.Id == template.Id && !t.IsDefault);
            if (existingTheme != null)
            {
                // Update existing theme
                existingTheme.Name = template.Name;
                existingTheme.Description = template.Description;
                existingTheme.Theme = template.Theme.Clone();
                existingTheme.ModifiedDate = DateTime.Now;
            }
            else
            {
                // Add new theme
                existingTheme.Name = template.Name;
                template.IsDefault = false;
                template.CreatedDate = DateTime.Now;
                template.ModifiedDate = DateTime.Now;
                _themeTemplates.Add(template);
            }

            SaveThemesToSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes a theme template
    /// </summary>
    public void DeleteTheme(ThemeTemplate template)
    {
        if (template == null || template.IsDefault) return;

        try
        {
            _themeTemplates.Remove(template);
            
            // If this was the current theme, switch to default
            if (CurrentTheme == template)
            {
                CurrentTheme = _themeTemplates.FirstOrDefault(t => t.IsDefault);
            }

            SaveThemesToSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the current theme and saves it to settings
    /// </summary>
    public void SetCurrentTheme(ThemeTemplate template)
    {
        if (template == null) return;

        try
        {
            CurrentTheme = template;
            Settings.Default.CurrentTerminalTheme = template.Name;
            Settings.Default.Save();

            // Trigger cloud sync if enabled
            _ = UserSettingsService.TriggerCloudSyncIfEnabled();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting current theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a theme to a terminal
    /// </summary>
    public void ApplyThemeToTerminal(ITerminal terminal, ThemeTemplate template)
    {
        if (terminal?.Theme == null || template?.Theme == null) return;

        try
        {
            // Copy theme properties to terminal
            terminal.Theme.FontFamily = template.Theme.FontFamily;
            terminal.Theme.FontSize = template.Theme.FontSize;
            terminal.Theme.Foreground = template.Theme.Foreground;
            terminal.Theme.Background = template.Theme.Background;
            terminal.Theme.SelectionColor = template.Theme.SelectionColor;
            terminal.Theme.CursorColor = template.Theme.CursorColor;
            terminal.Theme.CopySelectionColor = template.Theme.CopySelectionColor;

            terminal.RefreshTheme();
            SetCurrentTheme(template);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error applying theme to terminal: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets a theme by name
    /// </summary>
    public ThemeTemplate? GetThemeByName(string name)
    {
        return _themeTemplates.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Exports theme settings as JSON
    /// </summary>
    public string ExportTheme(ThemeTemplate template)
    {
        if (template?.Theme == null) return string.Empty;

        try
        {
            var dto = ConvertToDto(template);
            return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error exporting theme: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Imports theme from JSON
    /// </summary>
    public ThemeTemplate? ImportTheme(string jsonData)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<ThemeTemplateDto>(jsonData);
            if (dto != null)
            {
                var theme = ConvertFromDto(dto);
                if (theme != null)
                {
                    // Ensure unique name
                    var baseName = theme.Name;
                    var counter = 1;
                    while (_themeTemplates.Any(t => t.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        theme.Name = $"{baseName} ({counter})";
                        counter++;
                    }

                    SaveTheme(theme);
                    return theme;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error importing theme: {ex.Message}");
        }

        return null;
    }

    private void SaveThemesToSettings()
    {
        try
        {
            var customThemes = _themeTemplates.Where(t => !t.IsDefault).ToList();
            var themeDtos = customThemes.Select(ConvertToDto).ToList();
            var json = JsonSerializer.Serialize(themeDtos);
            
            Settings.Default.SavedTerminalThemes = json;
            Settings.Default.Save();

            // Trigger cloud sync if enabled
            _ = UserSettingsService.TriggerCloudSyncIfEnabled();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving themes to settings: {ex.Message}");
        }
    }

    private ThemeTemplateDto ConvertToDto(ThemeTemplate template)
    {
        return new ThemeTemplateDto
        {
            Name = template.Name,
            Description = template.Description,
            CreatedDate = template.CreatedDate,
            ModifiedDate = template.ModifiedDate,
            FontFamily = template.Theme.FontFamily.Source,
            FontSize = template.Theme.FontSize,
            ForegroundColor = ColorToHex((template.Theme.Foreground as SolidColorBrush)?.Color ?? Colors.White),
            BackgroundColor = ColorToHex((template.Theme.Background as SolidColorBrush)?.Color ?? Colors.Black),
            SelectionColor = ColorToHex(template.Theme.SelectionColor),
            CursorColor = ColorToHex((template.Theme.CursorColor as SolidColorBrush)?.Color ?? Colors.White),
            CopySelectionColor = ColorToHex(template.Theme.CopySelectionColor)
        };
    }

    private ThemeTemplate? ConvertFromDto(ThemeTemplateDto dto)
    {
        try
        {
            return new ThemeTemplate
            {
                Name = dto.Name,
                Description = dto.Description,
                CreatedDate = dto.CreatedDate,
                ModifiedDate = dto.ModifiedDate,
                IsDefault = false,
                Theme = new TerminalTheme
                {
                    FontFamily = new FontFamily(dto.FontFamily),
                    FontSize = dto.FontSize,
                    Foreground = new SolidColorBrush(HexToColor(dto.ForegroundColor)),
                    Background = new SolidColorBrush(HexToColor(dto.BackgroundColor)),
                    SelectionColor = HexToColor(dto.SelectionColor),
                    CursorColor = new SolidColorBrush(HexToColor(dto.CursorColor)),
                    CopySelectionColor = HexToColor(dto.CopySelectionColor)
                }
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error converting theme from DTO: {ex.Message}");
            return null;
        }
    }

    private string ColorToHex(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private Color HexToColor(string hex)
    {
        try
        {
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);

            if (hex.Length == 8)
            {
                return Color.FromArgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16),
                    Convert.ToByte(hex.Substring(6, 2), 16));
            }
            else if (hex.Length == 6)
            {
                return Color.FromRgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }
        }
        catch { }
        
        return Colors.Black;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Data transfer object for theme serialization
/// </summary>
public class ThemeTemplateDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 16.0;
    public string ForegroundColor { get; set; } = "#FFFFFFFF";
    public string BackgroundColor { get; set; } = "#FF000000";
    public string SelectionColor { get; set; } = "#500078FF";
    public string CursorColor { get; set; } = "#FFFFFFFF";
    public string CopySelectionColor { get; set; } = "#B490EE90";
}