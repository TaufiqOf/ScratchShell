using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace ScratchShell.UserControls.ThemeControl;

public partial class EditThemeUserControl : ContentDialog
{
    public static readonly DependencyProperty TerminalProperty = DependencyProperty.Register(
        nameof(Terminal), typeof(ITerminal), typeof(EditThemeUserControl),
        new PropertyMetadata(null, OnTerminalChanged));

    public static readonly DependencyProperty PreviewThemeProperty = DependencyProperty.Register(
        nameof(PreviewTheme), typeof(TerminalTheme), typeof(EditThemeUserControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ThemeTemplateProperty = DependencyProperty.Register(
        nameof(ThemeTemplate), typeof(ThemeTemplate), typeof(EditThemeUserControl),
        new PropertyMetadata(null, OnThemeTemplateChanged));

    public static readonly DependencyProperty IsNewThemeProperty = DependencyProperty.Register(
        nameof(IsNewTheme), typeof(bool), typeof(EditThemeUserControl),
        new PropertyMetadata(false));

    public ITerminal Terminal
    {
        get => (ITerminal)GetValue(TerminalProperty);
        set => SetValue(TerminalProperty, value);
    }

    public TerminalTheme PreviewTheme
    {
        get => (TerminalTheme)GetValue(PreviewThemeProperty);
        set => SetValue(PreviewThemeProperty, value);
    }

    public ThemeTemplate ThemeTemplate
    {
        get => (ThemeTemplate)GetValue(ThemeTemplateProperty);
        set => SetValue(ThemeTemplateProperty, value);
    }

    public bool IsNewTheme
    {
        get => (bool)GetValue(IsNewThemeProperty);
        set => SetValue(IsNewThemeProperty, value);
    }

    private TerminalTheme _theme => Terminal?.Theme;
    
    // Separate preview theme that doesn't affect the main terminal until Apply is pressed
    private TerminalTheme _previewTheme;

    // Event to notify completion of theme editing
    public event Action<ThemeTemplate?, bool>? ThemeEditCompleted;

    // Result property to return the edited theme
    public ThemeTemplate? ResultTheme { get; private set; }
    public bool WasCancelled { get; private set; }

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

    public EditThemeUserControl(IContentDialogService contentDialogService) : base(contentDialogService.GetDialogHost())
    {
        InitializeComponent();
        Loaded += EditThemeUserControl_Loaded;
        CreateColorPresets();
        
        // Initialize preview theme with defaults if no theme is available yet
        EnsurePreviewThemeExists();
    }

    private static void OnThemeTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditThemeUserControl ctrl && e.NewValue is ThemeTemplate template)
        {
            ctrl.InitializeFromTemplate(template);
        }
    }

    private void InitializeFromTemplate(ThemeTemplate template)
    {
        if (template?.Theme != null)
        {
            _previewTheme = template.Theme.Clone();
            UpdatePreviewThemeBinding();
        }
    }

    private void EnsurePreviewThemeExists()
    {
        if (_previewTheme == null)
        {
            // Create a default preview theme
            _previewTheme = new TerminalTheme
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 16.0,
                Foreground = Brushes.LightGray,
                Background = Brushes.Black,
                SelectionColor = Color.FromArgb(80, 0, 120, 255),
                CursorColor = Brushes.LightGray,
                CopySelectionColor = Color.FromArgb(180, 144, 238, 144)
            };
            
            UpdatePreviewThemeBinding();
        }
    }

    private void EditThemeUserControl_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate font families
        FontFamilyComboBox.ItemsSource = Fonts.SystemFontFamilies
            .Where(f => !string.IsNullOrEmpty(f.Source))
            .OrderBy(f => f.Source);

        // Set default color type selection
        ColorTypeComboBox.SelectedIndex = 0;

        // Initialize theme details if we have a template
        if (ThemeTemplate != null)
        {
            ThemeNameTextBox.Text = ThemeTemplate.Name;
            ThemeDescriptionTextBox.Text = ThemeTemplate.Description;
            
            _previewTheme = ThemeTemplate.Theme.Clone();
            UpdatePreviewThemeBinding();
        }
        else
        {
            // Set default values for new theme
            ThemeNameTextBox.Text = IsNewTheme ? "New Theme" : "";
            ThemeDescriptionTextBox.Text = IsNewTheme ? "Custom theme" : "";
            
            // Initialize from the PreviewTheme property if set
            if (PreviewTheme != null)
            {
                _previewTheme = PreviewTheme.Clone();
                UpdatePreviewThemeBinding();
            }
            else
            {
                // Ensure we have a preview theme
                EnsurePreviewThemeExists();

                if (_theme != null)
                {
                    // Initialize preview theme as a copy of the current theme
                    InitializePreviewTheme();
                }
            }
        }
        
        // Validate theme colors before applying
        ValidateThemeColors();

        // Set font family
        FontFamilyComboBox.SelectedItem = _previewTheme.FontFamily;

        // Set font size
        var sizeItem = FontSizeComboBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Content.ToString() == _previewTheme.FontSize.ToString());
        if (sizeItem != null)
            FontSizeComboBox.SelectedItem = sizeItem;

        UpdateColorPreviews();
        UpdatePreview();
        UpdateColorPickerFromTheme();
    }

    private void InitializePreviewTheme()
    {
        if (_theme == null) return;
        
        // Create a copy of the current theme for preview purposes
        _previewTheme = new TerminalTheme
        {
            FontFamily = _theme.FontFamily,
            FontSize = _theme.FontSize,
            Foreground = _theme.Foreground,
            Background = _theme.Background,
            SelectionColor = _theme.SelectionColor,
            CursorColor = _theme.CursorColor,
            CopySelectionColor = _theme.CopySelectionColor
        };
        
        // Update the public property for binding
        UpdatePreviewThemeBinding();
    }

    /// <summary>
    /// Helper method to update the preview theme properties
    /// Since TerminalTheme now implements INotifyPropertyChanged, we can modify the existing instance
    /// </summary>
    private void UpdatePreviewThemeBinding()
    {
        if (_previewTheme == null) return;
        
        // Just set the reference - no need to create a new instance
        PreviewTheme = _previewTheme;
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
                ToolTip = CreateColorTooltip(color),
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

    private string CreateColorTooltip(Color color)
    {
        return $"RGB({color.R}, {color.G}, {color.B})\nHex: #{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void OnTerminalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditThemeUserControl ctrl)
        {
            ctrl.UpdateUIFromTheme();
        }
    }

    private void UpdateUIFromTheme()
    {
        EnsurePreviewThemeExists();
        
        if (_theme != null)
        {
            // Initialize preview theme if not already done
            if (_previewTheme == null)
            {
                InitializePreviewTheme();
            }
        }

        // Update UI to reflect the current preview theme
        if (FontFamilyComboBox != null)
            FontFamilyComboBox.SelectedItem = _previewTheme.FontFamily;

        if (FontSizeComboBox != null)
        {
            var sizeItem = FontSizeComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(item => item.Content.ToString() == _previewTheme.FontSize.ToString());
            if (sizeItem != null)
                FontSizeComboBox.SelectedItem = sizeItem;
        }

        // Update preview and ensure binding reference is set
        UpdateColorPreviews();
        UpdatePreview();
        UpdateColorPickerFromTheme();
        UpdatePreviewThemeBinding(); // Still needed to set the initial reference
    }

    private void UpdateColorPreviews()
    {
        if (_previewTheme == null) return;

        // Update foreground preview
        if (ForegroundPreview != null)
        {
            ForegroundPreview.Background = _previewTheme.Background;
            if (ForegroundPreview.Child is TextBlock foregroundText)
            {
                foregroundText.Foreground = _previewTheme.Foreground;
            }
        }

        // Update background preview
        if (BackgroundPreview != null)
            BackgroundPreview.Background = _previewTheme.Background;

        // Update selection preview
        if (SelectionPreview != null)
            SelectionPreview.Background = new SolidColorBrush(_previewTheme.SelectionColor);

        // Update cursor preview
        if (CursorPreview != null)
            CursorPreview.Background = _previewTheme.CursorColor ?? _previewTheme.Foreground;

        // Update copy selection preview
        if (CopySelectionPreview != null)
            CopySelectionPreview.Background = new SolidColorBrush(_previewTheme.CopySelectionColor);
    }

    private void UpdatePreview()
    {
        if (_previewTheme == null) return;

        // Update the comprehensive terminal preview
        if (TerminalPreview != null)
        {
            TerminalPreview.UpdatePreview(_previewTheme);
        }
    }

    private void UpdateColorPickerFromTheme()
    {
        if (_previewTheme == null || MainColorPicker == null) return;

        _isUpdatingColorPicker = true;

        var currentColor = GetCurrentColor();
        MainColorPicker.SelectedColor = currentColor;

        _isUpdatingColorPicker = false;
    }

    private Color GetCurrentColor()
    {
        if (_previewTheme == null) return Colors.Black;

        return _currentColorType switch
        {
            "Foreground" => (_previewTheme.Foreground as SolidColorBrush)?.Color ?? Colors.White,
            "Background" => (_previewTheme.Background as SolidColorBrush)?.Color ?? Colors.Black,
            "Selection" => _previewTheme.SelectionColor,
            "Cursor" => (_previewTheme.CursorColor as SolidColorBrush)?.Color ?? (_previewTheme.Foreground as SolidColorBrush)?.Color ?? Colors.White,
            "CopySelection" => _previewTheme.CopySelectionColor,
            _ => Colors.Black
        };
    }

    private void ApplyColorToCurrentType(Color color)
    {
        if (_previewTheme == null) return;

        switch (_currentColorType)
        {
            case "Foreground":
                _previewTheme.Foreground = new SolidColorBrush(color);
                break;

            case "Background":
                _previewTheme.Background = new SolidColorBrush(color);
                break;

            case "Selection":
                _previewTheme.SelectionColor = color;
                break;

            case "Cursor":
                _previewTheme.CursorColor = new SolidColorBrush(color);
                break;

            case "CopySelection":
                _previewTheme.CopySelectionColor = color;
                break;
        }

        // The bindings will automatically update due to INotifyPropertyChanged
        // We still update the color previews and call UpdatePreview for the direct method call
        UpdateColorPreviews();
        UpdatePreview();
    }

    private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_previewTheme != null && FontFamilyComboBox.SelectedItem is FontFamily fontFamily)
        {
            _previewTheme.FontFamily = fontFamily;
            // The binding will automatically update due to INotifyPropertyChanged
            UpdatePreview();
        }
    }

    private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_previewTheme != null && FontSizeComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            if (double.TryParse(selectedItem.Content.ToString(), out double size))
            {
                _previewTheme.FontSize = size;
                // The binding will automatically update due to INotifyPropertyChanged
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


    private TerminalTheme CreateDefaultTheme()
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

    private void ResetThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaults();
    }

    private void DarkThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPredefinedThemeToPreview("dark");
    }

    private void LightThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPredefinedThemeToPreview("light");
    }

    private void MonokaiThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPredefinedThemeToPreview("monokai");
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
        if (_previewTheme == null) return;

        // Ensure all required colors have valid values
        if (_previewTheme.Foreground == null)
            _previewTheme.Foreground = Brushes.LightGray;

        if (_previewTheme.Background == null)
            _previewTheme.Background = Brushes.Black;

        if (_previewTheme.CursorColor == null)
            _previewTheme.CursorColor = _previewTheme.Foreground;

        // Validate alpha values for transparency colors
        if (_previewTheme.SelectionColor.A == 0)
            _previewTheme.SelectionColor = Color.FromArgb(80, _previewTheme.SelectionColor.R, _previewTheme.SelectionColor.G, _previewTheme.SelectionColor.B);

        if (_previewTheme.CopySelectionColor.A == 0)
            _previewTheme.CopySelectionColor = Color.FromArgb(180, _previewTheme.CopySelectionColor.R, _previewTheme.CopySelectionColor.G, _previewTheme.CopySelectionColor.B);

        // Property changes will automatically notify bindings
    }

    // Method to reset theme to defaults
    private void ResetToDefaults()
    {
        if (_previewTheme == null) return;

        _previewTheme.FontFamily = new FontFamily("Consolas");
        _previewTheme.FontSize = 16.0;
        _previewTheme.Foreground = Brushes.LightGray;
        _previewTheme.Background = Brushes.Black;
        _previewTheme.SelectionColor = Color.FromArgb(80, 0, 120, 255);
        _previewTheme.CursorColor = Brushes.LightGray;
        _previewTheme.CopySelectionColor = Color.FromArgb(180, 144, 238, 144);

        // Update UI to reflect the changes
        UpdateUIFromTheme();
        UpdatePreview();
    }

    // Method to apply a predefined theme to preview only
    private void ApplyPredefinedThemeToPreview(string themeName)
    {
        if (_previewTheme == null) return;

        switch (themeName.ToLower())
        {
            case "dark":
                _previewTheme.FontFamily = new FontFamily("Consolas");
                _previewTheme.FontSize = 16.0;
                _previewTheme.Foreground = new SolidColorBrush(Color.FromRgb(248, 248, 242));
                _previewTheme.Background = new SolidColorBrush(Color.FromRgb(40, 44, 52));
                _previewTheme.SelectionColor = Color.FromArgb(80, 98, 114, 164);
                _previewTheme.CursorColor = new SolidColorBrush(Color.FromRgb(248, 248, 242));
                _previewTheme.CopySelectionColor = Color.FromArgb(180, 87, 227, 137);
                break;

            case "light":
                _previewTheme.FontFamily = new FontFamily("Consolas");
                _previewTheme.FontSize = 16.0;
                _previewTheme.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
                _previewTheme.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                _previewTheme.SelectionColor = Color.FromArgb(80, 0, 120, 215);
                _previewTheme.CursorColor = new SolidColorBrush(Color.FromRgb(0, 0, 0));
                _previewTheme.CopySelectionColor = Color.FromArgb(180, 0, 150, 0);
                break;

            case "monokai":
                _previewTheme.FontFamily = new FontFamily("Consolas");
                _previewTheme.FontSize = 16.0;
                _previewTheme.Foreground = new SolidColorBrush(Color.FromRgb(248, 248, 242));
                _previewTheme.Background = new SolidColorBrush(Color.FromRgb(39, 40, 34));
                _previewTheme.SelectionColor = Color.FromArgb(80, 73, 72, 62);
                _previewTheme.CursorColor = new SolidColorBrush(Color.FromRgb(249, 38, 114));
                _previewTheme.CopySelectionColor = Color.FromArgb(180, 102, 217, 239);
                break;

            default:
                ResetToDefaults();
                return;
        }

        // Update UI to reflect the changes
        UpdateUIFromTheme();
        UpdatePreview();
    }

    protected override void OnButtonClick(ContentDialogButton button)
    {
        if (button == ContentDialogButton.Primary)
        {
            // Validate theme name
            if (string.IsNullOrWhiteSpace(ThemeNameTextBox?.Text))
            {
                // Show error message or focus on name field
                ThemeNameTextBox?.Focus();
                return;
            }
            ResultTheme = new ThemeTemplate
            {
                Id = ThemeTemplate.Id,
                Name = ThemeNameTextBox.Text.Trim(),
                Description = ThemeDescriptionTextBox?.Text?.Trim() ?? string.Empty,
                Theme = _previewTheme?.Clone() ?? CreateDefaultTheme(),
                IsDefault = false,
                CreatedDate = IsNewTheme ? DateTime.Now : (ThemeTemplate?.CreatedDate ?? DateTime.Now),
                ModifiedDate = DateTime.Now
            };
        }
        base.OnButtonClick(button);
    }
}