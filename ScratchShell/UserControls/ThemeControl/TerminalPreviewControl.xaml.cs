using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ScratchShell.UserControls.ThemeControl;

/// <summary>
/// A comprehensive terminal preview control that demonstrates all theme colors in action
/// </summary>
public partial class TerminalPreviewControl : UserControl
{
    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(TerminalTheme), typeof(TerminalPreviewControl),
        new PropertyMetadata(null, OnThemeChanged, CoerceThemeValue));

    public TerminalTheme Theme
    {
        get => (TerminalTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    private Storyboard _copyHighlightStoryboard;
    private DispatcherTimer _manualAnimationTimer;
    private int _animationStep = 0;
    private DateTime _animationStartTime;

    public TerminalPreviewControl()
    {
        InitializeComponent();
        
        // Set initial theme if available
        UpdateFromTheme();
        
        // Start the copy highlight animation when loaded with a small delay
        Loaded += (s, e) => 
        {
            // Use Dispatcher to ensure we're on the UI thread and add a small delay
            Dispatcher.BeginInvoke(() => StartCopyHighlightAnimation(), DispatcherPriority.Loaded);
        };
        
        // Stop animation when unloaded to prevent memory leaks
        Unloaded += (s, e) => StopCopyHighlightAnimation();
    }

    private void StartCopyHighlightAnimation()
    {
        if (Theme == null || CopyHighlightDemo == null) 
        {
            System.Diagnostics.Debug.WriteLine($"StartCopyHighlightAnimation: Theme={Theme}, CopyHighlightDemo={CopyHighlightDemo}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"Starting copy highlight animation with colors: Selection={Theme.SelectionColor}, Copy={Theme.CopySelectionColor}");

        // Stop any existing animation
        StopCopyHighlightAnimation();

        // For now, use the reliable manual animation approach
        // The storyboard approach has targeting issues with the Background.Color property
        System.Diagnostics.Debug.WriteLine("Using manual animation for reliable color transitions");
        StartManualAnimation();
        
        // Keep the storyboard code for future debugging if needed
        // bool storyboardWorked = TryStoryboardAnimation();
        // if (!storyboardWorked)
        // {
        //     System.Diagnostics.Debug.WriteLine("Storyboard failed, falling back to manual animation");
        //     StartManualAnimation();
        // }
    }

    private bool TryStoryboardAnimation()
    {
        try
        {
            // Create a new SolidColorBrush specifically for animation and ensure it's attached to the element
            var animationBrush = new SolidColorBrush(Theme.SelectionColor);
            
            // IMPORTANT: Register the brush as a resource so it can be found by the animation system
            animationBrush.SetValue(FrameworkElement.NameProperty, "AnimationBrush");
            CopyHighlightDemo.Background = animationBrush;

            // Create the storyboard for the looping animation
            _copyHighlightStoryboard = new Storyboard();
            
            // Create color animation sequence with pauses
            var colorAnimation = new ColorAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            // Define the animation sequence:
            // 0s: Start with SelectionColor
            colorAnimation.KeyFrames.Add(new DiscreteColorKeyFrame(Theme.SelectionColor, TimeSpan.Zero));
            
            // 0.8s: Fade to CopySelectionColor
            colorAnimation.KeyFrames.Add(new EasingColorKeyFrame(Theme.CopySelectionColor, TimeSpan.FromMilliseconds(800))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
            
            // 2.0s: Hold at CopySelectionColor (1.2s pause)
            colorAnimation.KeyFrames.Add(new DiscreteColorKeyFrame(Theme.CopySelectionColor, TimeSpan.FromMilliseconds(2000)));
            
            // 2.8s: Fade back to SelectionColor
            colorAnimation.KeyFrames.Add(new EasingColorKeyFrame(Theme.SelectionColor, TimeSpan.FromMilliseconds(2800))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
            
            // 4.0s: Hold at SelectionColor (1.2s pause) before loop restarts
            colorAnimation.KeyFrames.Add(new DiscreteColorKeyFrame(
                Theme.SelectionColor, 
                TimeSpan.FromMilliseconds(4000)));

            // Target the animation directly to the brush instead of through the element
            Storyboard.SetTarget(colorAnimation, animationBrush);
            Storyboard.SetTargetProperty(colorAnimation, new PropertyPath(SolidColorBrush.ColorProperty));

            // Add animation to storyboard
            _copyHighlightStoryboard.Children.Add(colorAnimation);

            // Start the animation - need to specify a namescope for the storyboard to find the target
            _copyHighlightStoryboard.Begin(CopyHighlightDemo, true);
            
            System.Diagnostics.Debug.WriteLine("Storyboard animation started successfully");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Storyboard animation failed: {ex.Message}");
            return false;
        }
    }

    private void StartManualAnimation()
    {
        // Initialize manual animation state
        _animationStep = 0;
        _animationStartTime = DateTime.Now;
        
        // Set initial color
        CopyHighlightDemo.Background = new SolidColorBrush(Theme.SelectionColor);
        
        // Create and start timer
        _manualAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50) // Update every 50ms for smooth animation
        };
        _manualAnimationTimer.Tick += ManualAnimationTimer_Tick;
        _manualAnimationTimer.Start();
        
        System.Diagnostics.Debug.WriteLine("Manual animation started");
    }

    private void ManualAnimationTimer_Tick(object sender, EventArgs e)
    {
        if (Theme == null || CopyHighlightDemo == null)
        {
            StopCopyHighlightAnimation();
            return;
        }

        var elapsed = DateTime.Now - _animationStartTime;
        var totalMs = elapsed.TotalMilliseconds;
        
        // 4-second cycle: 0-800ms fade to copy, 800-2000ms hold, 2000-2800ms fade back, 2800-4000ms hold
        var cycleMs = totalMs % 4000;
        
        Color currentColor;
        
        if (cycleMs <= 800)
        {
            // Fade from Selection to Copy (0-800ms)
            var progress = cycleMs / 800.0;
            currentColor = InterpolateColor(Theme.SelectionColor, Theme.CopySelectionColor, progress);
        }
        else if (cycleMs <= 2000)
        {
            // Hold at Copy (800-2000ms)
            currentColor = Theme.CopySelectionColor;
        }
        else if (cycleMs <= 2800)
        {
            // Fade from Copy back to Selection (2000-2800ms)
            var progress = (cycleMs - 2000) / 800.0;
            currentColor = InterpolateColor(Theme.CopySelectionColor, Theme.SelectionColor, progress);
        }
        else
        {
            // Hold at Selection (2800-4000ms)
            currentColor = Theme.SelectionColor;
        }
        
        // Update the background color
        if (CopyHighlightDemo.Background is SolidColorBrush brush)
        {
            brush.Color = currentColor;
        }
        else
        {
            CopyHighlightDemo.Background = new SolidColorBrush(currentColor);
        }
    }

    private Color InterpolateColor(Color from, Color to, double progress)
    {
        // Clamp progress to 0-1 range
        progress = Math.Max(0, Math.Min(1, progress));
        
        // Apply easing (sine ease in-out)
        progress = (Math.Sin((progress - 0.5) * Math.PI) + 1) / 2;
        
        return Color.FromArgb(
            (byte)(from.A + (to.A - from.A) * progress),
            (byte)(from.R + (to.R - from.R) * progress),
            (byte)(from.G + (to.G - from.G) * progress),
            (byte)(from.B + (to.B - from.B) * progress)
        );
    }

    private void StopCopyHighlightAnimation()
    {
        // Stop storyboard animation
        if (_copyHighlightStoryboard != null)
        {
            _copyHighlightStoryboard.Stop();
            _copyHighlightStoryboard = null;
        }
        
        // Stop manual animation
        if (_manualAnimationTimer != null)
        {
            _manualAnimationTimer.Stop();
            _manualAnimationTimer = null;
        }
        
        System.Diagnostics.Debug.WriteLine("Animation stopped");
    }

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalPreviewControl control)
        {
            // Since XAML bindings now handle the updates, we just need to invalidate the visual
            control.InvalidateVisual();
            
            // Also update the main border background if needed (fallback)
            if (control.MainBorder != null && control.Theme?.Background != null)
            {
                control.MainBorder.Background = control.Theme.Background;
            }

            // Restart animation with new theme colors
            control.StartCopyHighlightAnimation();
        }
    }

    private static object CoerceThemeValue(DependencyObject d, object value)
    {
        // This method is called whenever the Theme property value is being set
        return value;
    }

    private void UpdateFromTheme()
    {
        if (Theme == null) return;

        // The XAML bindings now handle most of the theme updates automatically
        // We just need to force a visual update and handle any special cases
        
        // Update the main border background as fallback (since it's also bound in XAML now)
        if (MainBorder != null)
        {
            MainBorder.Background = Theme.Background;
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
        // Animation will be restarted automatically via OnThemeChanged
        StartCopyHighlightAnimation();
    }

    public void Dispose()
    {
        StopCopyHighlightAnimation();
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