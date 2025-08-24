using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScratchShell.UserControls.ThemeControl;

/// <summary>
/// A comprehensive terminal preview control that demonstrates all theme colors in action
/// </summary>
public partial class TerminalPreviewControl : UserControl
{
    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(TerminalTheme), typeof(TerminalPreviewControl),
        new PropertyMetadata(null, OnThemeChanged));

    public TerminalTheme Theme
    {
        get => (TerminalTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    // Dependency properties for individual theme components
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(TerminalPreviewControl),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(TerminalPreviewControl),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(TerminalPreviewControl),
        new FrameworkPropertyMetadata(new FontFamily("Consolas"), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(TerminalPreviewControl),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionColorProperty = DependencyProperty.Register(
        nameof(SelectionColor), typeof(Color), typeof(TerminalPreviewControl),
        new FrameworkPropertyMetadata(Color.FromArgb(80, 0, 120, 255), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CursorColorProperty = DependencyProperty.Register(
        nameof(CursorColor), typeof(Brush), typeof(TerminalPreviewControl),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CopySelectionColorProperty = DependencyProperty.Register(
        nameof(CopySelectionColor), typeof(Color), typeof(TerminalPreviewControl),
        new FrameworkPropertyMetadata(Color.FromArgb(180, 144, 238, 144), FrameworkPropertyMetadataOptions.AffectsRender));

    private DispatcherTimer _animationTimer;
    private int _animationStep = 0;

    public new Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public new Brush Background
    {
        get => (Brush)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public new FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public new double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public Color SelectionColor
    {
        get => (Color)GetValue(SelectionColorProperty);
        set => SetValue(SelectionColorProperty, value);
    }

    public Brush CursorColor
    {
        get => (Brush)GetValue(CursorColorProperty);
        set => SetValue(CursorColorProperty, value);
    }

    public Color CopySelectionColor
    {
        get => (Color)GetValue(CopySelectionColorProperty);
        set => SetValue(CopySelectionColorProperty, value);
    }

    public TerminalPreviewControl()
    {
        InitializeComponent();
        
        // Set initial theme if available
        UpdateFromTheme();

        // Initialize animation timer
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _animationTimer.Tick += AnimationTimer_Tick;
        _animationTimer.Start();
    }

    private void AnimationTimer_Tick(object sender, EventArgs e)
    {
        // Cycle through different demonstration states
        _animationStep = (_animationStep + 1) % 4;
        
        switch (_animationStep)
        {
            case 0:
                // Show normal state
                if (SelectionDemo != null) SelectionDemo.Visibility = Visibility.Collapsed;
                if (CopyHighlightDemo != null) CopyHighlightDemo.Visibility = Visibility.Collapsed;
                break;
            case 1:
                // Show selection
                if (SelectionDemo != null) SelectionDemo.Visibility = Visibility.Visible;
                if (CopyHighlightDemo != null) CopyHighlightDemo.Visibility = Visibility.Collapsed;
                break;
            case 2:
                // Show copy highlight
                if (SelectionDemo != null) SelectionDemo.Visibility = Visibility.Collapsed;
                if (CopyHighlightDemo != null) CopyHighlightDemo.Visibility = Visibility.Visible;
                break;
            case 3:
                // Show both
                if (SelectionDemo != null) SelectionDemo.Visibility = Visibility.Visible;
                if (CopyHighlightDemo != null) CopyHighlightDemo.Visibility = Visibility.Visible;
                break;
        }
    }

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalPreviewControl control)
        {
            control.UpdateFromTheme();
        }
    }

    private void UpdateFromTheme()
    {
        if (Theme == null) return;

        // Update individual properties from theme
        Foreground = Theme.Foreground ?? Brushes.LightGray;
        Background = Theme.Background ?? Brushes.Black;
        FontFamily = Theme.FontFamily ?? new FontFamily("Consolas");
        FontSize = Theme.FontSize > 0 ? Theme.FontSize : 12.0;
        SelectionColor = Theme.SelectionColor;
        CursorColor = Theme.CursorColor ?? Theme.Foreground ?? Brushes.LightGray;
        CopySelectionColor = Theme.CopySelectionColor;

        // Update the main border background
        if (MainBorder != null)
        {
            MainBorder.Background = Background;
        }

        // Force a visual update
        InvalidateVisual();
    }

    /// <summary>
    /// Updates the preview with the specified theme
    /// </summary>
    /// <param name="theme">The terminal theme to preview</param>
    public void UpdatePreview(TerminalTheme theme)
    {
        Theme = theme;
    }

    /// <summary>
    /// Triggers a visual refresh of the preview
    /// </summary>
    public void RefreshPreview()
    {
        UpdateFromTheme();
        
        // Reset animation
        _animationStep = 0;
        if (_animationTimer != null)
        {
            _animationTimer.Stop();
            _animationTimer.Start();
        }
    }

    public void Dispose()
    {
        _animationTimer?.Stop();
        _animationTimer = null;
    }
}

/// <summary>
/// Converter to transform Color to SolidColorBrush for binding
/// </summary>
public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            return new SolidColorBrush(color);
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return Colors.Transparent;
    }
}