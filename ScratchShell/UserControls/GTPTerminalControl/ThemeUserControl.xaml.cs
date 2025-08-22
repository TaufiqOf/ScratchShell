using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;

namespace ScratchShell.UserControls.GTPTerminalControl;

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

    // Predefined color palette for quick selection
    private readonly Color[] _colorPalette = new Color[]
    {
        Colors.Black, Colors.White, Colors.Gray, Colors.LightGray, Colors.DarkGray,
        Colors.Red, Colors.DarkRed, Colors.Green, Colors.DarkGreen, Colors.Blue, Colors.DarkBlue,
        Colors.Yellow, Colors.Orange, Colors.Purple, Colors.Pink, Colors.Cyan, Colors.Magenta,
        Color.FromRgb(40, 44, 52),    // Dark background
        Color.FromRgb(248, 248, 242), // Light foreground
        Color.FromRgb(98, 114, 164),  // Blue selection
        Color.FromRgb(22, 22, 22),    // Very dark
        Color.FromRgb(255, 255, 255), // Pure white
    };

    public ThemeUserControl()
    {
        InitializeComponent();
        Loaded += ThemeUserControl_Loaded;
    }

    private void ThemeUserControl_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate font families
        FontFamilyComboBox.ItemsSource = Fonts.SystemFontFamilies
            .Where(f => !string.IsNullOrEmpty(f.Source))
            .OrderBy(f => f.Source);

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

    private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_theme != null && FontFamilyComboBox.SelectedItem is FontFamily fontFamily)
        {
            _theme.FontFamily = fontFamily;
            UpdatePreview();
            // Don't auto-apply theme on every change
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
                // Don't auto-apply theme on every change
            }
        }
    }

    private void ForegroundButton_Click(object sender, RoutedEventArgs e)
    {
        var color = ShowColorPickerDialog(_theme?.Foreground);
        if (color.HasValue)
        {
            _theme.Foreground = new SolidColorBrush(color.Value);
            UpdateColorPreviews();
            UpdatePreview();
        }
    }

    private void BackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        var color = ShowColorPickerDialog(_theme?.Background);
        if (color.HasValue)
        {
            _theme.Background = new SolidColorBrush(color.Value);
            UpdateColorPreviews();
            UpdatePreview();
        }
    }

    private void SelectionButton_Click(object sender, RoutedEventArgs e)
    {
        var color = ShowColorPickerDialog(new SolidColorBrush(_theme?.SelectionColor ?? Colors.Blue));
        if (color.HasValue)
        {
            _theme.SelectionColor = color.Value;
            UpdateColorPreviews();
            UpdatePreview();
        }
    }

    private Color? ShowColorPickerDialog(Brush? currentBrush)
    {
        var currentColor = Colors.Black;
        if (currentBrush is SolidColorBrush solidBrush)
        {
            currentColor = solidBrush.Color;
        }

        // Create a simple color picker window
        var colorPickerWindow = new Window
        {
            Title = "Choose Color",
            Width = 320,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Style = (Style)Application.Current.FindResource(typeof(Window)) // Use app theme
        };

        var mainPanel = new StackPanel { Margin = new Thickness(16) };
        
        // Color preview
        var previewBorder = new Border
        {
            Height = 60,
            Background = new SolidColorBrush(currentColor),
            BorderBrush = new SolidColorBrush(Colors.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 16)
        };
        mainPanel.Children.Add(previewBorder);

        // Color palette
        var palettePanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        foreach (var color in _colorPalette)
        {
            var colorButton = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                ToolTip = $"RGB({color.R}, {color.G}, {color.B})"
            };
            colorButton.Click += (s, e) =>
            {
                currentColor = color;
                previewBorder.Background = new SolidColorBrush(currentColor);
            };
            palettePanel.Children.Add(colorButton);
        }
        mainPanel.Children.Add(palettePanel);

        // RGB sliders
        var rgbPanel = new StackPanel();
        
        var redSlider = CreateColorSlider("Red:", currentColor.R, (value) => {
            currentColor = Color.FromRgb((byte)value, currentColor.G, currentColor.B);
            previewBorder.Background = new SolidColorBrush(currentColor);
        });
        var greenSlider = CreateColorSlider("Green:", currentColor.G, (value) => {
            currentColor = Color.FromRgb(currentColor.R, (byte)value, currentColor.B);
            previewBorder.Background = new SolidColorBrush(currentColor);
        });
        var blueSlider = CreateColorSlider("Blue:", currentColor.B, (value) => {
            currentColor = Color.FromRgb(currentColor.R, currentColor.G, (byte)value);
            previewBorder.Background = new SolidColorBrush(currentColor);
        });

        rgbPanel.Children.Add(redSlider);
        rgbPanel.Children.Add(greenSlider);
        rgbPanel.Children.Add(blueSlider);
        mainPanel.Children.Add(rgbPanel);

        // Buttons
        var buttonPanel = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        
        var okButton = new Button 
        { 
            Content = "OK", 
            Width = 80, 
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var cancelButton = new Button 
        { 
            Content = "Cancel", 
            Width = 80,
            IsCancel = true
        };

        bool dialogResult = false;
        okButton.Click += (s, e) => { dialogResult = true; colorPickerWindow.Close(); };
        cancelButton.Click += (s, e) => { dialogResult = false; colorPickerWindow.Close(); };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        mainPanel.Children.Add(buttonPanel);

        colorPickerWindow.Content = mainPanel;
        colorPickerWindow.ShowDialog();

        return dialogResult ? currentColor : null;
    }

    private StackPanel CreateColorSlider(string label, byte initialValue, System.Action<double> onValueChanged)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        
        var headerPanel = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            Margin = new Thickness(0, 0, 0, 4) 
        };
        
        var labelBlock = new TextBlock 
        { 
            Text = label, 
            FontWeight = FontWeights.Medium,
            Width = 50
        };
        
        var valueBlock = new TextBlock 
        { 
            Text = initialValue.ToString(),
            FontWeight = FontWeights.Normal,
            Foreground = new SolidColorBrush(Colors.Gray)
        };
        
        headerPanel.Children.Add(labelBlock);
        headerPanel.Children.Add(valueBlock);
        
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 255,
            Value = initialValue,
            TickFrequency = 1,
            IsSnapToTickEnabled = true
        };
        
        slider.ValueChanged += (s, e) => 
        {
            onValueChanged(e.NewValue);
            valueBlock.Text = ((int)e.NewValue).ToString();
        };
        
        panel.Children.Add(headerPanel);
        panel.Children.Add(slider);
        
        return panel;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        Terminal?.RefreshTheme();
    }
}
