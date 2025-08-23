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
    }

    private void UpdatePreview()
    {
        if (_theme == null) return;

        PreviewBorder.Background = _theme.Background;
        PreviewText.Foreground = _theme.Foreground;
        PreviewText.FontFamily = _theme.FontFamily;
        PreviewText.FontSize = _theme.FontSize;
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

    private void MainColorPicker_SelectedColorChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingColorPicker)
        {
            ApplyColorToCurrentType(MainColorPicker.SelectedColor);
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

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        Terminal?.RefreshTheme();
    }

    private void MainColorPicker_ColorChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingColorPicker)
        {
            ApplyColorToCurrentType(MainColorPicker.SelectedColor);
        }
    }
}