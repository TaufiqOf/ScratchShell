using ScratchShell.UserControls.TerminalControl;
using System;
using System.Linq;
using System.Printing;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using XtermSharp;

namespace ScratchShell.UserControls.GTPTerminalControl;

public partial class GPTTerminalUserControl : UserControl, ITerminal, ITerminalDelegate
{
    private Terminal? _terminal;
    private int _cols = 80;
    private int _rows = 24;
    private double _charWidth = 8;
    private double _charHeight = 16;
    private Typeface _typeface = new Typeface("Consolas");

    private const double TerminalFontSize = 16; // Fixed font size for terminal

    public GPTTerminalUserControl()
    {
        InitializeComponent();
        Loaded += GPTTerminalUserControl_Loaded;
    }

    private void GPTTerminalUserControl_Loaded(object sender, RoutedEventArgs e)
    {
        this.Focus();
        this.Focusable = true;
        this.IsTabStop = true;
        InitializeTerminalEmulator();

        UpdateCharSize();

        int cols = Math.Max(10, (int)(ActualWidth / _charWidth));
        int rows = Math.Max(2, (int)(ActualHeight / _charHeight));
        _terminal?.Resize(cols, rows);
        TerminalSizeChanged?.Invoke(this, new Size(ActualWidth, ActualHeight));

        RedrawTerminal();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_terminal != null)
        {
            UpdateCharSize();

            int cols = Math.Max(10, (int)(sizeInfo.NewSize.Width / _charWidth));
            int rows = Math.Max(2, (int)(sizeInfo.NewSize.Height / _charHeight));
            _terminal.Resize(cols, rows);
            TerminalSizeChanged?.Invoke(this, sizeInfo.NewSize);
            RedrawTerminal();
        }
    }

    private void UpdateCharSize()
    {
        var formattedText = new FormattedText(
            "W@gy",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            TerminalFontSize,
            Brushes.Black,
            new NumberSubstitution(),
            1.0);

        _charWidth = formattedText.WidthIncludingTrailingWhitespace / formattedText.Text.Length;
        _charHeight = formattedText.Height;
    }

    private void InitializeTerminalEmulator()
    {
        _terminal = new Terminal(this, new TerminalOptions { Cols = _cols, Rows = _rows });
    }

    public string InputLineSyntax { get => string.Empty; set { } }
    public bool IsReadOnly { get; set; }
    public string Text => string.Empty;

    public event ITerminal.TerminalCommandHandler CommandEntered;
    public event ITerminal.TerminalSizeHandler TerminalSizeChanged;

    public void AddOutput(string output)
    {
        if (_terminal == null) return;
        if (output.Any(c => c == '\0')) {
            var bytes = Encoding.Unicode.GetBytes(output);
            output = Encoding.UTF8.GetString(bytes);
        }
        _terminal.Feed(output);
        RedrawTerminal();
        ScrollToBottom();
    }

    public void AddInput(string input)
    {
        if (IsReadOnly || _terminal == null) return;
        _terminal.Feed(input);
        RedrawTerminal();
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        TerminalScrollViewer.ScrollToEnd();
    }

    private void TerminalControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsReadOnly || _terminal == null) return;

        int promptEnd = GetPromptEndCol();
        var buffer = _terminal.Buffer;
        int cursorCol = buffer.X;
        bool isNavKey = e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down ||
                        e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown;
        // Block editing before/at prompt
        if (!isNavKey && cursorCol < promptEnd)
        {
            e.Handled = true;
            return;
        }
        string keyToSend = string.Empty;
        if (e.Key == Key.Enter)
        {
            // Extract the current input line from the buffer
            string inputLine = GetCurrentInputLine();
            CommandEntered?.Invoke(this, inputLine);
            keyToSend = "\r";
        }
        else if (e.Key == Key.Back)
        {
            // Only allow backspace if cursor is after prompt
            if (cursorCol > promptEnd)
            {
                // Move cursor left
                _terminal.SetCursor(cursorCol - 1, buffer.Y);
                // Clear character at new cursor position
                var line = buffer.Lines[buffer.Y + buffer.YBase];
                if (cursorCol - 1 < line.Length)
                {
                    var cell = line[cursorCol - 1];
                    cell.Code = 0; // or ' '
                    line[cursorCol - 1] = cell;
                }
                RedrawTerminal();
            }
            e.Handled = true;
            return;
        }
        else
        {
            switch (e.Key)
            {
                case Key.Tab: keyToSend = "\t"; break;
                case Key.Delete: keyToSend = "\u001B[3~"; break;
                case Key.Escape: keyToSend = "\u001B"; break;
                case Key.Up: keyToSend = "\u001B[A"; break;
                case Key.Down: keyToSend = "\u001B[B"; break;
                case Key.Right: keyToSend = "\u001B[C"; break;
                case Key.Left: keyToSend = "\u001B[D"; break;
                case Key.Home: keyToSend = "\u001B[H"; break;
                case Key.End: keyToSend = "\u001B[F"; break;
                case Key.PageUp: keyToSend = "\u001B[5~"; break;
                case Key.PageDown: keyToSend = "\u001B[6~"; break;
                case Key.F1: keyToSend = "\u001BOP"; break;
                case Key.F2: keyToSend = "\u001BOQ"; break;
                case Key.F3: keyToSend = "\u001BOR"; break;
                case Key.F4: keyToSend = "\u001BOS"; break;
                case Key.F5: keyToSend = "\u001B[15~"; break;
                case Key.F6: keyToSend = "\u001B[17~"; break;
                case Key.F7: keyToSend = "\u001B[18~"; break;
                case Key.F8: keyToSend = "\u001B[19~"; break;
                case Key.F9: keyToSend = "\u001B[20~"; break;
                case Key.F10: keyToSend = "\u001B[21~"; break;
                case Key.F11: keyToSend = "\u001B[23~"; break;
                case Key.F12: keyToSend = "\u001B[24~"; break;
                default:
                    if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
                    {
                        CommandEntered?.Invoke(this, "\u0003");
                        e.Handled = true;
                        return;

                    }
                    else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.A && e.Key <= Key.Z)
                    {
                        int ctrlCode = (int)e.Key - (int)Key.A + 1;
                        keyToSend = ((char)ctrlCode).ToString();
                    }
                    else
                    {
                        var c = KeyToChar(e.Key, Keyboard.Modifiers);
                        if (c != null)
                            keyToSend = c.ToString();
                    }
                    break;
            }
        }
        if (!string.IsNullOrEmpty(keyToSend))
        {
            _terminal.Feed(keyToSend);
            RedrawTerminal();
        }
        e.Handled = true;
    }

    private int GetPromptEndCol()
    {
        if (_terminal == null) return 0;
        var buffer = _terminal.Buffer;
        var line = buffer.Lines[buffer.Y + buffer.YBase];
        int lastPrompt = -1;
        for (int i = 0; i < line.Length; i++)
        {
            var cell = line[i];
            char c = cell.Code == 0 ? ' ' : (char)cell.Code;
            if (c == '$' || c == '#')
                lastPrompt = i;
        }
        return lastPrompt + 1; // position after prompt
    }

    private string GetCurrentInputLine()
    {
        if (_terminal == null) return string.Empty;
        var buffer = _terminal.Buffer;
        var line = buffer.Lines[buffer.Y + buffer.YBase];
        var sb = new StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            var cell = line[i];
            char c = cell.Code == 0 ? ' ' : (char)cell.Code;
            sb.Append(c);
        }
        var text = sb.ToString().TrimEnd();
        // Find last $ or # and return everything after it
        int promptIdx = Math.Max(text.LastIndexOf('$'), text.LastIndexOf('#'));
        if (promptIdx >= 0 && promptIdx + 1 < text.Length)
            return text.Substring(promptIdx + 1).Trim();
        return text;
    }

    private void TerminalControl_MouseDown(object sender, MouseButtonEventArgs e)
    {
        TerminalCanvas.Focus();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        // Cleanup if needed
    }

    private static Brush[] AnsiForeground = new Brush[] {
        Brushes.Black, Brushes.DarkRed, Brushes.DarkGreen, Brushes.Olive, Brushes.DarkBlue, Brushes.DarkMagenta, Brushes.DarkCyan, Brushes.LightGray,
        Brushes.DarkGray, Brushes.Red, Brushes.Green, Brushes.Yellow, Brushes.Blue, Brushes.Magenta, Brushes.Cyan, Brushes.White
    };
    private static Brush[] AnsiBackground = new Brush[] {
        Brushes.Black, Brushes.DarkRed, Brushes.DarkGreen, Brushes.Olive, Brushes.DarkBlue, Brushes.DarkMagenta, Brushes.DarkCyan, Brushes.LightGray,
        Brushes.DarkGray, Brushes.Red, Brushes.Green, Brushes.Yellow, Brushes.Blue, Brushes.Magenta, Brushes.Cyan, Brushes.White
    };

    private static Brush GetAnsiForeground(int attr, bool inverse)
    {
        int fg = (attr >> 9) & 0x1ff;
        int bg = attr & 0x1ff;
        if (inverse) (fg, bg) = (bg, fg);
        if (fg >= 0 && fg < AnsiForeground.Length) return AnsiForeground[fg];
        return Brushes.LightGray;
    }
    private static Brush GetAnsiBackground(int attr, bool inverse)
    {
        int fg = (attr >> 9) & 0x1ff;
        int bg = attr & 0x1ff;
        if (inverse) (fg, bg) = (bg, fg);
        if (bg >= 0 && bg < AnsiBackground.Length) return AnsiBackground[bg];
        return Brushes.Black;
    }
    private static bool IsBold(int attr) => ((attr >> 18) & 1) != 0;
    private static bool IsUnderline(int attr) => ((attr >> 18) & 4) != 0;
    private static bool IsInverse(int attr) => ((attr >> 18) & 0x40) != 0;

    private void RedrawTerminal()
    {
        if (_terminal == null) return;
        TerminalCanvas.Children.Clear();
        var buffer = _terminal.Buffer;
        for (int row = 0; row < _terminal.Rows; row++)
        {
            var line = buffer.Lines[row + buffer.YBase];
            for (int col = 0; col < _terminal.Cols; col++)
            {
                char ch = ' ';
                int attr = XtermSharp.CharData.DefaultAttr;
                if (col < line.Length)
                {
                    var cell = line[col];
                    ch = cell.Code != 0 ? (char)cell.Code : ' ';
                    attr = cell.Attribute;
                }
                bool isCursor = (row == buffer.Y && col == buffer.X);
                bool inverse = IsInverse(attr) ^ isCursor;
                Brush fg = GetAnsiForeground(attr, inverse);
                Brush bg = GetAnsiBackground(attr, inverse);
                FontWeight weight = IsBold(attr) ? FontWeights.Bold : FontWeights.Normal;
                TextDecorationCollection deco = IsUnderline(attr) ? TextDecorations.Underline : null;
                if (isCursor)
                {
                    var rect = new Rectangle
                    {
                        Width = _charWidth,
                        Height = _charHeight,
                        Fill = fg,
                        Opacity = 0.8
                    };
                    Canvas.SetLeft(rect, col * _charWidth);
                    Canvas.SetTop(rect, row * _charHeight);
                    TerminalCanvas.Children.Add(rect);
                    var tb = new TextBlock
                    {
                        Text = ch.ToString(),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = TerminalFontSize,
                        Foreground = bg,
                        Background = Brushes.Transparent,
                        FontWeight = weight,
                        TextDecorations = deco
                    };
                    Canvas.SetLeft(tb, col * _charWidth);
                    Canvas.SetTop(tb, row * _charHeight);
                    TerminalCanvas.Children.Add(tb);
                }
                else
                {
                    var tb = new TextBlock
                    {
                        Text = ch.ToString(),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = TerminalFontSize,
                        Foreground = fg,
                        Background = bg,
                        FontWeight = weight,
                        TextDecorations = deco
                    };
                    Canvas.SetLeft(tb, col * _charWidth);
                    Canvas.SetTop(tb, row * _charHeight);
                    TerminalCanvas.Children.Add(tb);
                }
            }
        }
        // Hide the default cursor if present
        if (Cursor != null)
            Cursor.Visibility = Visibility.Collapsed;
    }

    private static char? KeyToChar(Key key, ModifierKeys modifiers)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            char c = (char)('a' + (key - Key.A));
            if ((modifiers & ModifierKeys.Shift) != 0)
                c = char.ToUpper(c);
            return c;
        }
        if (key >= Key.D0 && key <= Key.D9)
        {
            char c = (char)('0' + (key - Key.D0));
            return c;
        }
        if (key == Key.Space) return ' ';
        return null;
    }

    // ITerminalDelegate implementation (minimal)
    public void ShowCursor(Terminal terminal) { }
    public void SetTerminalTitle(Terminal terminal, string title) { }
    public void SetTerminalIconTitle(Terminal terminal, string title) { }
    public void SizeChanged(Terminal terminal) { }
    public void Send(byte[] data) { }
    public string WindowCommand(Terminal terminal, XtermSharp.WindowManipulationCommand cmd, params int[] args) => string.Empty;
    public bool IsProcessTrusted() => true;
}
