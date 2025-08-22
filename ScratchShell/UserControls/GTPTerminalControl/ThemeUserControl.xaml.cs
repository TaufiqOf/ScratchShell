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

    public ThemeUserControl()
    {
        InitializeComponent();
        Loaded += ThemeUserControl_Loaded;
    }

    private void ThemeUserControl_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate font families
        FontFamilyComboBox.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
        if (_theme != null)
        {
            FontFamilyComboBox.SelectedItem = _theme.FontFamily;
            FontSizeTextBox.Text = _theme.FontSize.ToString();
            ForegroundButton.Background = _theme.Foreground;
            BackgroundButton.Background = _theme.Background;
            SelectionButton.Background = new SolidColorBrush(_theme.SelectionColor);
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
            FontSizeTextBox.Text = _theme.FontSize.ToString();
            ForegroundButton.Background = _theme.Foreground;
            BackgroundButton.Background = _theme.Background;
            SelectionButton.Background = new SolidColorBrush(_theme.SelectionColor);
        }
    }

    private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_theme != null && FontFamilyComboBox.SelectedItem is FontFamily fontFamily)
        {
            _theme.FontFamily = fontFamily;
            Terminal?.RefreshTheme();
        }
    }

    private void FontSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_theme != null && double.TryParse(FontSizeTextBox.Text, out double size))
        {
            _theme.FontSize = size;
            Terminal?.RefreshTheme();
        }
    }

    private void ForegroundButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.ColorDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var color = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            _theme.Foreground = new SolidColorBrush(color);
            ForegroundButton.Background = _theme.Foreground;
            Terminal?.RefreshTheme();
        }
    }

    private void BackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.ColorDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var color = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            _theme.Background = new SolidColorBrush(color);
            BackgroundButton.Background = _theme.Background;
            Terminal?.RefreshTheme();
        }
    }

    private void SelectionButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.ColorDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var color = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            _theme.SelectionColor = color;
            SelectionButton.Background = new SolidColorBrush(color);
            Terminal?.RefreshTheme();
        }
    }
}
