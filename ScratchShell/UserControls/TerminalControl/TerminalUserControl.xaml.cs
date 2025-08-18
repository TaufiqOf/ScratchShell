using ScratchShell.UserControls.TerminalControl;
using ScratchShell.UserControls.TerminalControl.Parser;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScratchShell.UserControls.TerminalControl;

/// <summary>
/// Interaction logic for TerminalControl.xaml
/// </summary>
public partial class TerminalUserControl : UserControl
{
    public string InputLineSyntax { get; set; } = "";
    public string Output => (new TextRange(TerminalBox.Document?.ContentStart, TerminalBox.Document?.ContentEnd)).Text;

    private Paragraph? promptParagraph;
    private TextPointer? promptStart;

    public TerminalState TerminalState { get; set; }

    public event Action<TerminalUserControl, string>? CommandEntered;
    public event Action<TerminalUserControl, string>? TitleChanged;
    public event Action<TerminalUserControl, Size>? SizeChanged;

    public bool IsReadOnly
    {
        get { return (bool)GetValue(IsReadOnlyProperty); }
        set
        {
            SetValue(IsReadOnlyProperty, value);
            TerminalBox.IsReadOnly = value;
        }
    }

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register("IsReadOnly", typeof(bool), typeof(TerminalUserControl), new PropertyMetadata(true));




    public string Text
    {
        get { return new TextRange(TerminalBox.Document.ContentStart, TerminalBox.Document.ContentEnd).Text; }
    }


    public TerminalUserControl()
    {
        InitializeComponent();
        Loaded += TerminalControl_Loaded;

        TerminalBox.PreviewKeyDown += TerminalBox_PreviewKeyDown;
        TerminalBox.SizeChanged += TerminalBoxSizeChanged;
        TerminalState = new TerminalState();
        TerminalState.PropertyChanged += TerminalState_PropertyChanged;
        // Bind copy/paste commands
        TerminalBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, CopyCommand_Executed, CopyCommand_CanExecute));
        TerminalBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, PasteCommand_Executed, PasteCommand_CanExecute));
        AddOutput("");
    }

    private void TerminalBoxSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SizeChanged?.Invoke(this, e.NewSize);
    }

    public void Clear()
    {
        TerminalBox.Document.Blocks.Clear();
    }
    
    public void AddOutput(string text)
    {
        if(string.Equals(Text,text))
        {
            return;
        }
        var parser = new AnsiParser();
        var renderer = new TextRenderer();
        var control = new AnsiControlParser();

        // Parse ANSI segments for rendering
        var segments = parser.Parse(text.TrimEnd('\r', '\n'));
        var paragraph = new Paragraph { Margin = new Thickness(0) };

        foreach (var run in renderer.Render(segments))
            paragraph.Inlines.Add(run);

        if (!string.IsNullOrEmpty(InputLineSyntax))
        {
            paragraph.Inlines.Add(new Run("\r\n" + InputLineSyntax)
            {
                Foreground = Brushes.White,
                FontWeight = FontWeights.Normal
            });
        }

        TerminalBox.Document.Blocks.Clear();
        TerminalBox.Document.Blocks.Add(paragraph);

        promptStart = paragraph.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
        promptParagraph = paragraph;
        TerminalBox.CaretPosition = promptStart;
        TerminalBox.Focus();

        // After rendering, handle control sequences in the input text
        control.ParseAndHandle(text, TerminalState);
    }

    private void TerminalControl_Loaded(object sender, RoutedEventArgs e)
    {
        TerminalBox.IsReadOnly = IsReadOnly;
        ContextMenu menu = new ContextMenu();

        var copyItem = new MenuItem
        {
            Header = "Copy",
            Command = ApplicationCommands.Copy
        };
        menu.Items.Add(copyItem);

        var pasteItem = new MenuItem
        {
            Header = "Paste",
            Command = ApplicationCommands.Paste
        };
        menu.Items.Add(pasteItem);

        // Do NOT add Cut

        TerminalBox.ContextMenu = menu;
        //AddOutput(InputLineSyntax);
    }

    private void TerminalBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string input = GetCurrentInput();
            CommandEntered?.Invoke(this, input);

            e.Handled = true;
        }
        else if (e.Key != Key.Up || e.Key != Key.Left || e.Key != Key.Right || e.Key != Key.Down || e.Key != Key.Home)
        {
            if (promptStart == null)
                return;
            // Prevent deleting or moving before prompt
            if ((!IsCaretAfterPrompt() && !((e.Key == Key.LeftShift || e.Key == Key.Right || e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl || e.Key == Key.Up || e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Down) || e.Key == Key.Home))
                || (e.Key == Key.Back && !IsBackAfterPrompt()))
            {
                TerminalBox.CaretPosition = promptStart;
                e.Handled = true;
            }
        }
    }

    private void CutCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = false;
        e.Handled = true;
    }

    private void CutCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private string GetCurrentInput()
    {
        if (promptStart == null || promptParagraph == null)
            return "";

        var inputRange = new TextRange(promptStart, promptParagraph.ContentEnd);
        string input = inputRange.Text.TrimEnd('\r', '\n');

        Debug.WriteLine($"Raw Input: [{input}]");
        return input;
    }

    private bool IsCaretAfterPrompt()
    {
        if (promptStart == null)
            return false;

        var caretPos = TerminalBox.CaretPosition;

        if (caretPos.Paragraph != promptStart.Paragraph)
            return false;

        int offset = promptStart.GetOffsetToPosition(caretPos);
        return offset >= 0;
    }

    private bool IsBackAfterPrompt()
    {
        if (promptStart == null)
            return false;

        var caretPos = TerminalBox.CaretPosition;

        if (caretPos.Paragraph != promptStart.Paragraph)
            return false;

        int offset = promptStart.GetOffsetToPosition(caretPos);
        return offset > 0;
    }

    private void TerminalState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Ensure UI updates happen on UI thread
        Dispatcher.InvokeAsync(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(TerminalState.CursorVisible):
                    UpdateCursorVisibility();
                    break;

                case nameof(TerminalState.WindowTitle):
                    UpdateWindowTitle();
                    break;

                case nameof(TerminalState.CursorRow):
                case nameof(TerminalState.CursorCol):
                    UpdateCursorPosition();
                    break;

                case nameof(TerminalState.ForegroundColor):
                case nameof(TerminalState.BackgroundColor):
                case nameof(TerminalState.ColorPalette):
                    UpdateColors();
                    break;

                    // Add cases for other properties as needed
            }
        }, DispatcherPriority.Background);
    }
    private void UpdateCursorVisibility()
    {
        TerminalBox.Cursor = TerminalState.CursorVisible ? Cursors.IBeam : Cursors.None;
    }

    private void UpdateWindowTitle()
    {
        TitleChanged?.Invoke(this, TerminalState.WindowTitle);
    }

    private void UpdateCursorPosition()
    {
        if (TerminalBox.Document == null)
            return;

        // Ensure enough paragraphs exist for the requested row
        var paragraphs = TerminalBox.Document.Blocks.OfType<Paragraph>().ToList();
        while (paragraphs.Count <= TerminalState.CursorRow)
        {
            var newParagraph = new Paragraph(new Run(""));
            TerminalBox.Document.Blocks.Add(newParagraph);
            paragraphs.Add(newParagraph);
        }

        Paragraph targetParagraph = paragraphs[TerminalState.CursorRow];
        if (targetParagraph == null)
            return;

        // Get current paragraph text
        string paragraphText = new TextRange(targetParagraph.ContentStart, targetParagraph.ContentEnd).Text;

        // Pad paragraph with spaces if CursorCol is beyond current length
        if (TerminalState.CursorCol > paragraphText.Length)
        {
            string padding = new string(' ', TerminalState.CursorCol - paragraphText.Length);
            targetParagraph.Inlines.Add(new Run(padding));
            paragraphText = new TextRange(targetParagraph.ContentStart, targetParagraph.ContentEnd).Text;
        }

        // Clamp col index
        int col = Math.Max(0, Math.Min(TerminalState.CursorCol, paragraphText.Length));

        // Find TextPointer at column
        TextPointer pointer = targetParagraph.ContentStart;
        for (int i = 0; i < col && pointer != null; i++)
        {
            pointer = pointer.GetNextInsertionPosition(LogicalDirection.Forward);
            if (pointer == null)
                break;
        }

        if (pointer != null)
        {
            TerminalBox.CaretPosition = pointer;
            TerminalBox.Focus();

            // Ensure caret is visible
            //TerminalBox.ScrollToHome(); // optional: reset scroll first
            //TerminalBox.CaretPosition.Paragraph?.BringIntoView();
            //TerminalBox.ScrollToVerticalOffset(TerminalBox.VerticalOffset + 1); // nudge to ensure caret shows
        }
    }



    private void UpdateColors()
    {
        // Update TerminalBox foreground/background colors

        if (!string.IsNullOrEmpty(TerminalState.ForegroundColor))
        {
            try
            {
                TerminalBox.Foreground = (Brush)new BrushConverter().ConvertFromString(TerminalState.ForegroundColor);
            }
            catch { /* ignore invalid color */ }
        }

        if (!string.IsNullOrEmpty(TerminalState.BackgroundColor))
        {
            try
            {
                TerminalBox.Background = (Brush)new BrushConverter().ConvertFromString(TerminalState.BackgroundColor);
            }
            catch { /* ignore invalid color */ }
        }

        // You may want to refresh text colors in the document as well,
        // or handle palette colors in renderer.
    }
    private void CopyCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = TerminalBox.Selection != null && !TerminalBox.Selection.IsEmpty;
        e.Handled = true;
    }

    private void CopyCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (TerminalBox.Selection != null && !TerminalBox.Selection.IsEmpty)
        {
            Clipboard.SetText(TerminalBox.Selection.Text);
        }
        e.Handled = true;
    }

    private void PasteCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        // Allow paste only if control is editable
        e.CanExecute = !IsReadOnly;
        e.Handled = true;
    }

    private void PasteCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (IsReadOnly || promptStart == null)
        {
            e.Handled = true;
            return;
        }

        if (Clipboard.ContainsText())
        {
            string pasteText = Clipboard.GetText(TextDataFormat.Text);

            // sanitize: strip newlines if you want single-line pastes only
            pasteText = pasteText.Replace("\r", "").Replace("\n", "");

            // insert only at or after prompt
            if (!IsCaretAfterPrompt())
            {
                TerminalBox.CaretPosition = TerminalBox.Document.ContentEnd;
            }

            TerminalBox.CaretPosition.InsertTextInRun(pasteText);
        }

        e.Handled = true;
    }
    private void TerminalBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {

    }
}