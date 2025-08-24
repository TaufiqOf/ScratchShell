using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ScratchShell.UserControls.ThemeControl;

public partial class ThemeUserControl : UserControl
{
    public static readonly DependencyProperty TerminalProperty = DependencyProperty.Register(
        nameof(Terminal), typeof(ITerminal), typeof(ThemeUserControl),
        new PropertyMetadata(null, OnTerminalChanged));

    public ITerminal Terminal
    {
        get => (ITerminal)GetValue(TerminalProperty);
        set => SetValue(TerminalProperty, value);
    }

    private TerminalTheme _theme => Terminal?.Theme;

    // Predefined color palette for quick selection with terminal-focused colors
    private readonly Color[] _colorPalette = new Color[]
    {
        // Common terminal colors
        Colors.Black, Colors.White, Colors.Gray, Colors.LightGray, Colors.DarkGray,
        Colors.Red, Colors.DarkRed, Colors.Green, Colors.DarkGreen, Colors.Blue, Colors.DarkBlue,
        Colors.Yellow, Colors.Orange, Colors.Purple, Colors.Pink, Colors.Cyan, Colors.Magenta,

        // Popular terminal theme colors
        Color.FromRgb(40, 44, 52),    // Atom One Dark background
        Color.FromRgb(248, 248, 242), // Atom One Dark foreground
        Color.FromRgb(98, 114, 164),  // Blue selection
        Color.FromRgb(22, 22, 22),    // Very dark background
        Color.FromRgb(255, 255, 255), // Pure white
        Color.FromRgb(46, 52, 54),    // Tango dark
        Color.FromRgb(211, 215, 207), // Tango light
        Color.FromRgb(87, 227, 137),  // Terminal green
        Color.FromRgb(249, 38, 114),  // Monokai pink
        Color.FromRgb(102, 217, 239), // Monokai cyan
    };

    // Track which color is currently being edited
    private string _currentColorType = "Foreground";

    private bool _isUpdatingColorPicker = false;

    public ThemeUserControl()
    {
        InitializeComponent();
        Loaded += ThemeUserControl_Loaded;
        CreateColorPresets();
    }

    private void ThemeUserControl_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate font families
        FontFamilyComboBox.ItemsSource = Fonts.SystemFontFamilies
            .Where(f => !string.IsNullOrEmpty(f.Source))
            .OrderBy(f => f.Source);

        // Set default color type selection
        ColorTypeComboBox.SelectedIndex = 0;

        if (_theme != null)
        {
            // Validate theme colors before applying
            ValidateThemeColors();

            // Set font family
            FontFamilyComboBox.SelectedItem = _theme.FontFamily;

            // Set font size
            var sizeItem = FontSizeComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(item => item.Content.ToString() == _theme.FontSize.ToString());
            if (sizeItem != null)
                FontSizeComboBox.SelectedItem = sizeItem;

            UpdateColorPreviews();
            UpdatePreview();
            UpdateColorPickerFromTheme();
        }
    }

    private void CreateColorPresets()
    {
        ColorPresetsPanel.Children.Clear();

        foreach (var color in _colorPalette)
        {
            var colorButton = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                ToolTip = $"RGB({color.R}, {color.G}, {color.B})\nHex: #{color.R:X2}{color.G:X2}{color.B:X2}",
                Style = null, // Remove any button styling
                Cursor = Cursors.Hand
            };

            // Add hover effect
            colorButton.MouseEnter += (s, e) =>
            {
                colorButton.BorderBrush = new SolidColorBrush(Colors.Black);
                colorButton.BorderThickness = new Thickness(2);
            };
            colorButton.MouseLeave += (s, e) =>
            {
                colorButton.BorderBrush = new SolidColorBrush(Colors.Gray);
                colorButton.BorderThickness = new Thickness(1);
            };

            colorButton.Click += (s, e) =>
            {
                _isUpdatingColorPicker = true;
                MainColorPicker.SelectedColor = color;
                _isUpdatingColorPicker = false;
                ApplyColorToCurrentType(color);
            };

            ColorPresetsPanel.Children.Add(colorButton);
        }
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
        if (_theme != null)
        {
            FontFamilyComboBox.SelectedItem = _theme.FontFamily;

            var sizeItem = FontSizeComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(item => item.Content.ToString() == _theme.FontSize.ToString());
            if (sizeItem != null)
                FontSizeComboBox.SelectedItem = sizeItem;

            UpdateColorPreviews();
            UpdatePreview();
            UpdateColorPickerFromTheme();
        }
    }

    private void UpdateColorPreviews()
    {
        if (_theme == null) return;

        // Update foreground preview
        ForegroundPreview.Background = _theme.Background;
        if (ForegroundPreview.Child is TextBlock foregroundText)
        {
            foregroundText.Foreground = _theme.Foreground;
        }

        // Update background preview
        BackgroundPreview.Background = _theme.Background;

        // Update selection preview
        SelectionPreview.Background = new SolidColorBrush(_theme.SelectionColor);

        // Update cursor preview
        CursorPreview.Background = _theme.CursorColor ?? _theme.Foreground;

        // Update copy selection preview
        CopySelectionPreview.Background = new SolidColorBrush(_theme.CopySelectionColor);
    }

    private void UpdatePreview()
    {
        if (_theme == null) return;

        // Update the comprehensive terminal preview
        var terminalPreview = FindName("TerminalPreview") as TerminalPreviewControl;
        if (terminalPreview != null)
        {
            terminalPreview.UpdatePreview(_theme);
        }
    }

    private void UpdateColorPickerFromTheme()
    {
        if (_theme == null) return;

        _isUpdatingColorPicker = true;

        var currentColor = GetCurrentColor();
        MainColorPicker.SelectedColor = currentColor;

        _isUpdatingColorPicker = false;
    }

    private Color GetCurrentColor()
    {
        if (_theme == null) return Colors.Black;

        return _currentColorType switch
        {
            "Foreground" => (_theme.Foreground as SolidColorBrush)?.Color ?? Colors.White,
            "Background" => (_theme.Background as SolidColorBrush)?.Color ?? Colors.Black,
            "Selection" => _theme.SelectionColor,
            "Cursor" => (_theme.CursorColor as SolidColorBrush)?.Color ?? (_theme.Foreground as SolidColorBrush)?.Color ?? Colors.White,
            "CopySelection" => _theme.CopySelectionColor,
            _ => Colors.Black
        };
    }

    private void ApplyColorToCurrentType(Color color)
    {
        if (_theme == null) return;

        switch (_currentColorType)
        {
            case "Foreground":
                _theme.Foreground = new SolidColorBrush(color);
                break;

            case "Background":
                _theme.Background = new SolidColorBrush(color);
                break;

            case "Selection":
                _theme.SelectionColor = color;
                break;

            case "Cursor":
                _theme.CursorColor = new SolidColorBrush(color);
                break;

            case "CopySelection":
                _theme.CopySelectionColor = color;
                break;
        }

        UpdateColorPreviews();
        UpdatePreview();
    }

    private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_theme != null && FontFamilyComboBox.SelectedItem is FontFamily fontFamily)
        {
            _theme.FontFamily = fontFamily;
            UpdatePreview();
        }
    }

    private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_theme != null && FontSizeComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            if (double.TryParse(selectedItem.Content.ToString(), out double size))
            {
                _theme.FontSize = size;
                UpdatePreview();
            }
        }
    }

    private void ColorTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColorTypeComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            _currentColorType = selectedItem.Tag?.ToString() ?? "Foreground";
            UpdateColorPickerFromTheme();
        }
    }

    private void MainColorPicker_ColorChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingColorPicker)
        {
            ApplyColorToCurrentType(MainColorPicker.SelectedColor);
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        Terminal?.RefreshTheme();
        
        // Optional: Save theme settings to user preferences
        SaveThemeToSettings();
    }

    private void SaveThemeToSettings()
    {
        // This could be implemented to save theme settings to user preferences
        // For now, we'll just apply the theme to the terminal
    }

    private void RefreshPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        // Force refresh the terminal preview
        var terminalPreview = FindName("TerminalPreview") as TerminalPreviewControl;
        if (terminalPreview != null)
        {
            terminalPreview.RefreshPreview();
        }
        UpdateColorPreviews();
    }

    private void ResetThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaults();
    }

    private void DarkThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPredefinedTheme("dark");
    }

    private void LightThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPredefinedTheme("light");
    }

    private void MonokaiThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPredefinedTheme("monokai");
    }

    /// <summary>
    /// Public method to get available predefined themes
    /// </summary>
    /// <returns>Array of available theme names</returns>
    public string[] GetAvailableThemes()
    {
        return new[] { "dark", "light", "monokai" };
    }

    /// <summary>
    /// Public method to apply a theme by name
    /// </summary>
    /// <param name="themeName">Name of the theme to apply</param>
    /// <returns>True if theme was applied successfully</returns>
    public bool ApplyTheme(string themeName)
    {
        try
        {
            ApplyPredefinedTheme(themeName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ForegroundPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ColorTypeComboBox.SelectedIndex = 0; // Select "Text Color"
    }

    private void BackgroundPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ColorTypeComboBox.SelectedIndex = 1; // Select "Background Color"
    }

    private void SelectionPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ColorTypeComboBox.SelectedIndex = 2; // Select "Selection Color"
    }

    private void CursorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ColorTypeComboBox.SelectedIndex = 3; // Select "Cursor Color"
    }

    private void CopySelectionPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ColorTypeComboBox.SelectedIndex = 4; // Select "Copy Highlight Color"
    }

    // Validate theme colors and provide defaults if needed
    private void ValidateThemeColors()
    {
        if (_theme == null) return;

        // Ensure all required colors have valid values
        if (_theme.Foreground == null)
            _theme.Foreground = Brushes.LightGray;

        if (_theme.Background == null)
            _theme.Background = Brushes.Black;

        if (_theme.CursorColor == null)
            _theme.CursorColor = _theme.Foreground;

        // Validate alpha values for transparency colors
        if (_theme.SelectionColor.A == 0)
            _theme.SelectionColor = Color.FromArgb(80, _theme.SelectionColor.R, _theme.SelectionColor.G, _theme.SelectionColor.B);

        if (_theme.CopySelectionColor.A == 0)
            _theme.CopySelectionColor = Color.FromArgb(180, _theme.CopySelectionColor.R, _theme.CopySelectionColor.G, _theme.CopySelectionColor.B);
    }

    // Method to export current theme settings
    public string ExportThemeSettings()
    {
        if (_theme == null) return string.Empty;

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            FontFamily = _theme.FontFamily.Source,
            FontSize = _theme.FontSize,
            Foreground = ColorToHex((_theme.Foreground as SolidColorBrush)?.Color ?? Colors.White),
            Background = ColorToHex((_theme.Background as SolidColorBrush)?.Color ?? Colors.Black),
            SelectionColor = ColorToHex(_theme.SelectionColor),
            CursorColor = ColorToHex((_theme.CursorColor as SolidColorBrush)?.Color ?? Colors.White),
            CopySelectionColor = ColorToHex(_theme.CopySelectionColor)
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    // Helper method to convert color to hex string
    private string ColorToHex(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    // Method to import theme settings from JSON
    public void ImportThemeSettings(string jsonSettings)
    {
        try
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<dynamic>(jsonSettings);
            // Implementation would parse JSON and apply settings
            // This is a placeholder for future enhancement
        }
        catch (Exception ex)
        {
            // Handle import errors gracefully
            System.Diagnostics.Debug.WriteLine($"Failed to import theme settings: {ex.Message}");
        }
    }

    // Method to reset theme to defaults
    private void ResetToDefaults()
    {
        if (_theme == null) return;

        _theme.FontFamily = new FontFamily("Consolas");
        _theme.FontSize = 16.0;
        _theme.Foreground = Brushes.LightGray;
        _theme.Background = Brushes.Black;
        _theme.SelectionColor = Color.FromArgb(80, 0, 120, 255);
        _theme.CursorColor = Brushes.LightGray;
        _theme.CopySelectionColor = Color.FromArgb(180, 144, 238, 144);

        UpdateUIFromTheme();
        UpdatePreview();
        Terminal?.RefreshTheme();
    }

    // Method to apply a predefined theme
    public void ApplyPredefinedTheme(string themeName)
    {
        if (_theme == null) return;

        switch (themeName.ToLower())
        {
            case "dark":
                _theme.FontFamily = new FontFamily("Consolas");
                _theme.FontSize = 16.0;
                _theme.Foreground = new SolidColorBrush(Color.FromRgb(248, 248, 242));
                _theme.Background = new SolidColorBrush(Color.FromRgb(40, 44, 52));
                _theme.SelectionColor = Color.FromArgb(80, 98, 114, 164);
                _theme.CursorColor = new SolidColorBrush(Color.FromRgb(248, 248, 242));
                _theme.CopySelectionColor = Color.FromArgb(180, 87, 227, 137);
                break;

            case "light":
                _theme.FontFamily = new FontFamily("Consolas");
                _theme.FontSize = 16.0;
                _theme.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
                _theme.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                _theme.SelectionColor = Color.FromArgb(80, 0, 120, 215);
                _theme.CursorColor = new SolidColorBrush(Color.FromRgb(0, 0, 0));
                _theme.CopySelectionColor = Color.FromArgb(180, 0, 150, 0);
                break;

            case "monokai":
                _theme.FontFamily = new FontFamily("Consolas");
                _theme.FontSize = 16.0;
                _theme.Foreground = new SolidColorBrush(Color.FromRgb(248, 248, 242));
                _theme.Background = new SolidColorBrush(Color.FromRgb(39, 40, 34));
                _theme.SelectionColor = Color.FromArgb(80, 73, 72, 62);
                _theme.CursorColor = new SolidColorBrush(Color.FromRgb(249, 38, 114));
                _theme.CopySelectionColor = Color.FromArgb(180, 102, 217, 239);
                break;

            default:
                ResetToDefaults();
                return;
        }

        UpdateUIFromTheme();
        UpdatePreview();
        Terminal?.RefreshTheme();
    }
}