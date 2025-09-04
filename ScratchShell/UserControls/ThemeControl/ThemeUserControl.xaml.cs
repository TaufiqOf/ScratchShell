using ScratchShell.Services;
using ScratchShell.View.Dialog;
using ScratchShell.ViewModels.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ScratchShell.UserControls.ThemeControl;

public partial class ThemeUserControl : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty TerminalProperty = DependencyProperty.Register(
        nameof(Terminal), typeof(ITerminal), typeof(ThemeUserControl),
        new PropertyMetadata(null, OnTerminalChanged));

    public static readonly DependencyProperty PreviewThemeProperty = DependencyProperty.Register(
        nameof(PreviewTheme), typeof(TerminalTheme), typeof(ThemeUserControl),
        new PropertyMetadata(null));

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

    private readonly ThemeManager _themeManager;

    public ObservableCollection<ThemeTemplate> ThemeTemplates => _themeManager.ThemeTemplates;

    public ThemeTemplate? SelectedTemplate
    {
        get => _themeManager.CurrentTheme;
        set
        {
            if (_themeManager.CurrentTheme != value)
            {
                _themeManager.CurrentTheme = value;
                OnPropertyChanged();
                UpdateButtonStates();
            }
        }
    }

    public IContentDialogService ContentDialogService { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ThemeUserControl()
    {
        _themeManager = ThemeManager.Instance;
        _themeManager.PropertyChanged += ThemeManager_PropertyChanged;
        
        InitializeComponent();
        DataContext = this;
        Loaded += ThemeUserControl_Loaded;
    }

    private void ThemeManager_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeManager.CurrentTheme))
        {
            OnPropertyChanged(nameof(SelectedTemplate));
            UpdateButtonStates();
        }
    }

    private void ThemeUserControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateButtonStates();
        
        // Apply current theme to terminal if available
        if (Terminal != null && _themeManager.CurrentTheme != null)
        {
            _themeManager.ApplyThemeToTerminal(Terminal, _themeManager.CurrentTheme);
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
        // Apply current theme to terminal when terminal changes
        if (Terminal != null && _themeManager.CurrentTheme != null)
        {
            _themeManager.ApplyThemeToTerminal(Terminal, _themeManager.CurrentTheme);
        }
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
            _themeManager.DeleteTheme(SelectedTemplate);
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

        ShowThemeEditorAsync(newTemplate, isNew: true);
    }

    private void EditTheme(ThemeTemplate template)
    {
        if (template.IsDefault)
        {
            // Create a copy for editing
            var editTemplate = template.Clone();
            editTemplate.Name = $"{template.Name} (Copy)";
            editTemplate.IsDefault = false;
            ShowThemeEditorAsync(editTemplate, isNew: true);
        }
        else
        {
            ShowThemeEditorAsync(template, isNew: false);
        }
    }

    private void ApplyTheme(ThemeTemplate template)
    {
        if (Terminal != null)
        {
            _themeManager.ApplyThemeToTerminal(Terminal, template);
        }
    }

    private async Task ShowThemeEditorAsync(ThemeTemplate template, bool isNew)
    {
        if (ContentDialogService is not null)
        {
            if (ContentDialogService is not null)
            {
                var serverContentDialog = new EditThemeUserControl(ContentDialogService);

                var contentDialogResult = await serverContentDialog.ShowAsync();
            }
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