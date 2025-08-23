using System;
using System.Diagnostics;
using System.Linq;
using System.Printing;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using XtermSharp;
using System.ComponentModel;
using System.Collections.Generic;
using ScratchShell.UserControls.ThemeControl;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.IO;
using System.Drawing.Imaging;

namespace ScratchShell.UserControls.GTPTerminalControl;

public partial class GPTTerminalUserControl : UserControl, ITerminal, ITerminalDelegate
{
    private Terminal? _terminal;
    private int _cols = 80;
    private int _rows = 24;
    private double _charWidth = 8;
    private double _charHeight = 16;
    private Typeface _typeface = new Typeface("Consolas");

    private const double ExtraScrollPadding = 48; // Extra space in pixels for scrolling past last line

    private System.Windows.Threading.DispatcherTimer _resizeRedrawTimer;
    private Size _pendingResizeSize;

    // Add a field to store the last rendered buffer snapshot
    private string[]? _lastRenderedBufferLines;

    // Selection state for copy/paste
    private bool _isSelecting = false;
    private (int row, int col)? _selectionStart = null;
    private (int row, int col)? _selectionEnd = null;
    private bool _isCopyHighlight = false;
    private SolidColorBrush _selectionBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0, 120, 255));
    private readonly System.Windows.Media.Color _defaultSelectionColor = System.Windows.Media.Color.FromArgb(80, 0, 120, 255);
    private readonly System.Windows.Media.Color _copySelectionColor = System.Windows.Media.Color.FromArgb(180, 144, 238, 144); // LightGreen
    private System.Windows.Threading.DispatcherTimer? _copyHighlightTimer;

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(TerminalTheme), typeof(GPTTerminalUserControl),
        new PropertyMetadata(new TerminalTheme(), OnThemeChanged));
    public TerminalTheme Theme
    {
        get => (TerminalTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public void RefreshTheme()
    {
        ApplyThemeProperties();
    }

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GPTTerminalUserControl ctrl)
        {
            ctrl.ApplyThemeProperties();
        }
    }

    public GPTTerminalUserControl()
    {
        InitializeComponent();
        Loaded += GPTTerminalUserControl_Loaded;
        _resizeRedrawTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _resizeRedrawTimer.Tick += (s, e) =>
        {
            _resizeRedrawTimer.Stop();
            if (_terminal != null)
            {
                UpdateTerminalLayoutAndSize(_pendingResizeSize);
            }
        };
    }

    private void GPTTerminalUserControl_Loaded(object sender, RoutedEventArgs e)
    {
        this.Focus();
        this.Focusable = true;
        this.IsTabStop = true;
        if (_terminal == null)
        {
            InitializeTerminalEmulator();
        }
        UpdateTerminalLayoutAndSize();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_terminal != null)
        {
            _pendingResizeSize = sizeInfo.NewSize;
            _resizeRedrawTimer.Stop();
            _resizeRedrawTimer.Start();
        }
    }

    private void UpdateCharSize()
    {
        var formattedText = new FormattedText(
            "W@gy",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(Theme.FontFamily.Source),
            Theme.FontSize,
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
        var buffer = _terminal.Buffer;
        int prevLineCount = buffer.Lines.Length;
        string[]? prevSnapshot = _lastRenderedBufferLines;
        _terminal.Feed(output);
        int newLineCount = buffer.Lines.Length;
        // Compare current buffer lines to previous snapshot and redraw only changed lines
        for (int row = 0; row < buffer.Lines.Length; row++)
        {
            string current = BufferLineToString(buffer.Lines[row], _terminal.Cols);
            string? prev = (prevSnapshot != null && row < prevSnapshot.Length) ? prevSnapshot[row] : null;
            if (prev == null || !current.Equals(prev))
            {
                RedrawTerminal(onlyRow: row);
            }
        }
        ScrollToBottom();
    }

    public void AddInput(string input)
    {
        if (IsReadOnly || _terminal == null) return;
        _terminal.Feed(input);
        // Redraw only the last line
        var buffer = _terminal.Buffer;
        int lastRow = buffer.Y + buffer.YBase;
        RedrawTerminal(onlyRow: lastRow);
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
                // Redraw only the last line
                int lastRow = buffer.Y + buffer.YBase;
                RedrawTerminal(onlyRow: lastRow);
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
            // Redraw only the last line
            int lastRow = buffer.Y + buffer.YBase;
            RedrawTerminal(onlyRow: lastRow);
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
        var pos = e.GetPosition(TerminalCanvas);
        int col = (int)(pos.X / _charWidth);
        int row = (int)(pos.Y / _charHeight);

        if (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Clear previous selection rectangles
            foreach (var rect in _selectionHighlightRects)
                TerminalCanvas.Children.Remove(rect);
            _selectionHighlightRects.Clear();
            // Start new selection
            _isSelecting = true;
            _selectionStart = (row, col);
            _selectionEnd = (row, col);
            UpdateSelectionHighlightRect();
            TerminalCanvas.CaptureMouse();
        }
        else if (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+Left Click: Copy selection
            if (_selectionStart.HasValue && _selectionEnd.HasValue)
            {
                string selectedText = GetSelectedText();
                if (!string.IsNullOrEmpty(selectedText))
                {
                    Clipboard.SetText(selectedText);
                    StartCopyHighlightTransition();
                }
            }
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            // Alt+Left Click: Paste clipboard at input area
            if (Clipboard.ContainsText())
            {
                string pasteText = Clipboard.GetText();
                PasteAtInputArea(pasteText);
            }
            e.Handled = true;
        }
    }

    private void StartCopyHighlightTransition()
    {
        _isCopyHighlight = true;
        var animToGreen = new ColorAnimation
        {
            From = _defaultSelectionColor,
            To = _copySelectionColor,
            Duration = TimeSpan.FromMilliseconds(400), // Smooth transition to green
            AutoReverse = false
        };
        animToGreen.Completed += (s, e) =>
        {
            // Hold green for 1.2s, then fade back
            _copyHighlightTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _copyHighlightTimer.Tick += (s2, e2) =>
            {
                _copyHighlightTimer.Stop();
                var animBack = new ColorAnimation
                {
                    From = _copySelectionColor,
                    To = _defaultSelectionColor,
                    Duration = TimeSpan.FromMilliseconds(600),
                    AutoReverse = false
                };
                animBack.Completed += (s3, e3) =>
                {
                    _isCopyHighlight = false;
                    // Restore color to default
                    foreach (var rect in _selectionHighlightRects)
                    {
                        if (rect.Fill is SolidColorBrush brush)
                            brush.Color = _defaultSelectionColor;
                    }
                };
                foreach (var rect in _selectionHighlightRects)
                {
                    if (rect.Fill is SolidColorBrush brush)
                        brush.BeginAnimation(SolidColorBrush.ColorProperty, animBack);
                }
            };
            _copyHighlightTimer.Start();
        };
        foreach (var rect in _selectionHighlightRects)
        {
            if (rect.Fill is SolidColorBrush brush)
                brush.BeginAnimation(SolidColorBrush.ColorProperty, animToGreen);
        }
    }

    private void TerminalCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(TerminalCanvas);
            int col = (int)(pos.X / _charWidth);
            int row = (int)(pos.Y / _charHeight);
            _selectionEnd = (row, col);
            UpdateSelectionHighlightRect();
        }
    }

    private void TerminalCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelecting && e.ChangedButton == MouseButton.Left)
        {
            _isSelecting = false;
            TerminalCanvas.ReleaseMouseCapture();
            // Finalize selection highlight
            UpdateSelectionHighlightRect();
        }
    }

    private readonly List<Rectangle> _selectionHighlightRects = new();

    private void UpdateSelectionHighlightRect()
    {
        foreach (var rect in _selectionHighlightRects)
        {
            TerminalCanvas.Children.Remove(rect);
        }
        _selectionHighlightRects.Clear();

        if (!_selectionStart.HasValue || !_selectionEnd.HasValue || _terminal == null)
            return;
        var (row1, col1) = _selectionStart.Value;
        var (row2, col2) = _selectionEnd.Value;
        int startRow = Math.Min(row1, row2);
        int endRow = Math.Max(row1, row2);
        int startCol = Math.Min(col1, col2);
        int endCol = Math.Max(col1, col2);

        int maxCol = _terminal.Cols - 1;

        for (int row = startRow; row <= endRow; row++)
        {
            int colStart = (row == startRow) ? startCol : 0;
            int colEnd = (row == endRow) ? endCol : maxCol;
            colStart = Math.Max(0, Math.Min(colStart, maxCol));
            colEnd = Math.Max(0, Math.Min(colEnd, maxCol));
            if (colEnd < colStart) continue;

            var rect = new Rectangle
            {
                Width = (colEnd - colStart + 1) * _charWidth,
                Height = _charHeight,
                Fill = _selectionBrush,
                Opacity = 0.5,
                IsHitTestVisible = false
            };
            TerminalCanvas.Children.Add(rect);
            Canvas.SetLeft(rect, colStart * _charWidth);
            Canvas.SetTop(rect, row * _charHeight);
            _selectionHighlightRects.Add(rect);
        }
    }

    private void DrawSelection()
    {
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue || _terminal == null) return;
        // If the canvas was just cleared (after RedrawTerminal), skip removal
        bool hasSelectionRects = false;
        for (int i = 0; i < TerminalCanvas.Children.Count; i++)
        {
            if (TerminalCanvas.Children[i] is Rectangle rect && rect.Tag as string == "SelectionRect")
            {
                hasSelectionRects = true;
                break;
            }
        }
        if (hasSelectionRects) {
            for (int i = TerminalCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (TerminalCanvas.Children[i] is Rectangle rect && rect.Tag as string == "SelectionRect")
                    TerminalCanvas.Children.RemoveAt(i);
            }
        }
        var (row1, col1) = _selectionStart.Value;
        var (row2, col2) = _selectionEnd.Value;
        int startRow = Math.Min(row1, row2);
        int endRow = Math.Max(row1, row2);
        int startCol = Math.Min(col1, col2);
        int endCol = Math.Max(col1, col2);
        int maxCol = _terminal.Cols - 1;
        for (int row = startRow; row <= endRow; row++)
        {
            int colStart = (row == startRow) ? startCol : 0;
            int colEnd = (row == endRow) ? endCol : maxCol;
            colStart = Math.Max(0, Math.Min(colStart, maxCol));
            colEnd = Math.Max(0, Math.Min(colEnd, maxCol));
            if (colEnd < colStart) continue;
            var rect = new Rectangle
            {
                Width = (colEnd - colStart + 1) * _charWidth,
                Height = _charHeight,
                Fill = _selectionBrush,
                IsHitTestVisible = false,
                Tag = "SelectionRect"
            };
            Canvas.SetLeft(rect, colStart * _charWidth);
            Canvas.SetTop(rect, row * _charHeight);
            TerminalCanvas.Children.Add(rect);
        }
    }

    private string GetSelectedText()
    {
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue || _terminal == null) return string.Empty;
        var (row1, col1) = _selectionStart.Value;
        var (row2, col2) = _selectionEnd.Value;
        int startRow = Math.Min(row1, row2);
        int endRow = Math.Max(row1, row2);
        int startCol = Math.Min(col1, col2);
        int endCol = Math.Max(col1, col2);
        var buffer = _terminal.Buffer;
        var sb = new StringBuilder();
        for (int row = startRow; row <= endRow; row++)
        {
            int colStart = (row == startRow) ? startCol : 0;
            int colEnd = (row == endRow) ? endCol : _cols - 1;
            if (row >= 0 && row < buffer.Lines.Length)
            {
                var line = buffer.Lines[row];
                var lineSb = new StringBuilder();
                for (int col = colStart; col <= colEnd && col < line.Length; col++)
                {
                    var cell = line[col];
                    char c = cell.Code != 0 ? (char)cell.Code : ' ';
                    lineSb.Append(c);
                }
                sb.Append(lineSb.ToString().TrimEnd());
            }
            if (row < endRow) sb.AppendLine();
        }
        return sb.ToString();
    }

    private void PasteAtInputArea(string text)
    {
        if (_terminal == null || IsReadOnly) return;
        // Insert at current cursor position in input area (last line)
        var buffer = _terminal.Buffer;
        int row = buffer.Y + buffer.YBase;
        int col = buffer.X;
        var line = buffer.Lines[row];
        // Insert text at cursor
        foreach (char c in text)
        {
            if (col < line.Length)
            {
                var cell = line[col];
                cell.Code = c;
                line[col] = cell;
            }
            col++;
        }
        buffer.X = col;
        RedrawTerminal(onlyRow: row);
    }

    // Change ApplyThemeProperties to public so ThemeUserControl can call it
    public void ApplyThemeProperties()
    {
        _typeface = new Typeface(Theme.FontFamily.Source);
        _selectionBrush = new SolidColorBrush(Theme.SelectionColor);
        if (TerminalCanvas != null)
            TerminalCanvas.Background = Theme.Background;
        // Only update char size and redraw, do not resize or clear buffer on theme change
        UpdateCharSize();
        RedrawTerminal();
        // Clear old selection highlights and selection state
        ClearSelectionHighlightRects();
        _selectionStart = null;
        _selectionEnd = null;
        // Do not draw selection after theme change
    }

    // Helper to clear selection highlight rectangles from RootGrid
    private void ClearSelectionHighlightRects()
    {
        foreach (var rect in _selectionHighlightRects)
        {
            TerminalCanvas.Children.Remove(rect);
        }
        _selectionHighlightRects.Clear();
    }

    // Cache last used control size to avoid unnecessary buffer resize on font change
    private Size _lastLayoutSize = Size.Empty;
    private int _lastCols = -1;
    private int _lastRows = -1;

    // Helper to update char size, canvas size, and terminal size (used for both resize and theme change)
    private void UpdateTerminalLayoutAndSize(Size? newSize = null)
    {
        UpdateCharSize();
        Size size = newSize ?? new Size(ActualWidth, ActualHeight);
        int cols = Math.Max(10, (int)(size.Width / _charWidth));
        int rows = Math.Max(2, (int)(size.Height / _charHeight));
        TerminalCanvas.Width = cols * _charWidth;
        TerminalCanvas.Height = rows * _charHeight;
        // Only resize terminal if control pixel size has changed
        if (_terminal != null && (size != _lastLayoutSize || cols != _lastCols || rows != _lastRows))
        {
            _terminal.Resize(cols, rows);
            _lastLayoutSize = size;
            _lastCols = cols;
            _lastRows = rows;
        }
        TerminalSizeChanged?.Invoke(this, size);
        RedrawTerminal();
        UpdateSelectionHighlightRect(); // Ensure selection area updates with resize
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

    // Helper to get a string representation of a buffer line
    private static string BufferLineToString(dynamic line, int cols)
    {
        var sb = new StringBuilder(cols);
        for (int i = 0; i < cols; i++)
        {
            char ch = ' ';
            if (i < line.Length)
            {
                var cell = line[i];
                ch = cell.Code != 0 ? (char)cell.Code : ' ';
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    // Redraws the terminal. If onlyRow is provided, only redraw that row.
    private void RedrawTerminal(int? onlyRow = null)
    {
        Debug.WriteLine($"RedrawTerminal called. onlyRow={onlyRow}");
        if (_terminal == null) return;
        var buffer = _terminal.Buffer;
        TerminalCanvas.Width = _terminal.Cols * _charWidth;
        double contentHeight = buffer.Lines.Length * _charHeight;
        double visibleHeight = _terminal.Rows * _charHeight;
        double canvasHeight = Math.Max(contentHeight, visibleHeight);
        TerminalCanvas.Height = canvasHeight;

        if (onlyRow.HasValue)
        {
            onlyRow = null;
        }

        int cols = _terminal.Cols;
        int rows = buffer.Lines.Length;
        double charWidth = _charWidth;
        double charHeight = _charHeight;
        // Extract and freeze all DependencyObject values on the UI thread
        var theme = Theme;
        var typeface = _typeface;
        var fgBrush = (theme.Foreground as SolidColorBrush)?.CloneCurrentValue() ?? Brushes.LightGray.CloneCurrentValue();
        var bgBrush = (theme.Background as SolidColorBrush)?.CloneCurrentValue() ?? Brushes.Black.CloneCurrentValue();
        fgBrush.Freeze();
        bgBrush.Freeze();
        var fontSize = theme.FontSize;
        var fontFamily = theme.FontFamily.Source; // Use string name

        int pixelWidth = (int)Math.Ceiling(cols * charWidth);
        int pixelHeight = (int)Math.Ceiling(rows * charHeight);
        Debug.WriteLine($"Bitmap size: {pixelWidth}x{pixelHeight}");

        Task.Run(() =>
        {
            try
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // Fill background
                    dc.DrawRectangle(bgBrush, null, new Rect(0, 0, pixelWidth, pixelHeight));
                    for (int row = 0; row < rows; row++)
                    {
                        var line = buffer.Lines[row];
                        for (int col = 0; col < cols; col++)
                        {
                            char ch = ' ';
                            int attr = XtermSharp.CharData.DefaultAttr;
                            if (col < line.Length)
                            {
                                var cell = line[col];
                                ch = cell.Code != 0 ? (char)cell.Code : ' ';
                                attr = cell.Attribute;
                            }
                            bool isCursor = (row == buffer.Y + buffer.YBase && col == buffer.X);
                            bool inverse = IsInverse(attr) ^ isCursor;
                            Brush fg;
                            Brush bg;
                            if (attr == XtermSharp.CharData.DefaultAttr)
                            {
                                fg = fgBrush;
                                bg = bgBrush;
                            }
                            else
                            {
                                fg = GetAnsiForeground(attr, inverse);
                                bg = GetAnsiBackground(attr, inverse);
                            }
                            FontWeight weight = IsBold(attr) ? FontWeights.Bold : FontWeights.Normal;
                            TextDecorationCollection deco = IsUnderline(attr) ? TextDecorations.Underline : null;
                            // Draw background for each cell
                            dc.DrawRectangle(bg, null, new Rect(col * charWidth, row * charHeight, charWidth, charHeight));
                            // Draw character
                            var ft = new FormattedText(
                                ch.ToString(),
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                new Typeface(new FontFamily(fontFamily), FontStyles.Normal, weight, FontStretches.Normal),
                                fontSize,
                                fg,
                                VisualTreeHelper.GetDpi(this).PixelsPerDip
                            );
                            dc.DrawText(ft, new Point(col * charWidth, row * charHeight));
                            // Draw cursor overlay
                            if (isCursor)
                            {
                                dc.DrawRectangle(fg, null, new Rect(col * charWidth, row * charHeight, charWidth, charHeight));
                                var ftCursor = new FormattedText(
                                    ch.ToString(),
                                    System.Globalization.CultureInfo.CurrentCulture,
                                    FlowDirection.LeftToRight,
                                    new Typeface(new FontFamily(fontFamily), FontStyles.Normal, weight, FontStretches.Normal),
                                    fontSize,
                                    bg,
                                    VisualTreeHelper.GetDpi(this).PixelsPerDip
                                );
                                dc.DrawText(ftCursor, new Point(col * charWidth, row * charHeight));
                            }
                        }
                    }
                }
                var bmp = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(dv);

                BitmapImage loadedImg = null;
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bmp));
                        encoder.Save(ms);
                        ms.Seek(0, SeekOrigin.Begin);
                        loadedImg = new BitmapImage();
                        loadedImg.BeginInit();
                        loadedImg.CacheOption = BitmapCacheOption.OnLoad;
                        loadedImg.StreamSource = ms;
                        loadedImg.EndInit();
                        loadedImg.Freeze();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Exception creating in-memory PNG: {ex}");
                }

                Dispatcher.Invoke(() =>
                {
                    TerminalBitmapImage.Source = loadedImg;
                    TerminalBitmapImage.Width = pixelWidth;
                    TerminalBitmapImage.Height = pixelHeight;
                    TerminalCanvas.Width = pixelWidth;
                    TerminalCanvas.Height = pixelHeight;
                    if (Cursor != null)
                        Cursor.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in RedrawTerminal Task: {ex}");
            }
        });
        _lastRenderedBufferLines = new string[buffer.Lines.Length];
        for (int row = 0; row < buffer.Lines.Length; row++)
        {
            _lastRenderedBufferLines[row] = BufferLineToString(buffer.Lines[row], _terminal.Cols);
        }
    }

    private static char? KeyToChar(Key key, ModifierKeys modifiers)
    {
        // Letters
        if (key >= Key.A && key <= Key.Z)
        {
            char c = (char)('a' + (key - Key.A));
            if ((modifiers & ModifierKeys.Shift) != 0)
                c = char.ToUpper(c);
            return c;
        }
        // Digits
        if (key >= Key.D0 && key <= Key.D9)
        {
            char c = (char)('0' + (key - Key.D0));
            // Handle shifted digits for symbols
            if ((modifiers & ModifierKeys.Shift) != 0)
            {
                string shifted = ")!@#$%^&*(";
                c = shifted[key - Key.D0];
            }
            return c;
        }
        // Space
        if (key == Key.Space) return ' ';
        // Oem keys for punctuation and symbols
        if (key >= Key.Oem1 && key <= Key.OemBackslash)
        {
            bool shift = (modifiers & ModifierKeys.Shift) != 0;
            switch (key)
            {
                case Key.Oem1: return shift ? ':' : ';';
                case Key.OemPlus: return shift ? '+' : '=';
                case Key.OemComma: return shift ? '<' : ',';
                case Key.OemMinus: return shift ? '_' : '-';
                case Key.OemPeriod: return shift ? '>' : '.';
                case Key.Oem2: return shift ? '?' : '/';
                case Key.Oem3: return shift ? '~' : '`';
                case Key.Oem4: return shift ? '{' : '[';
                case Key.Oem5: return shift ? '|' : '\\';
                case Key.Oem6: return shift ? '}' : ']';
                case Key.Oem7: return shift ? '"' : '\'';
                // Add more Oem keys if needed
            }
        }
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

    // Public methods for copy/paste operations
    public bool HasSelection()
    {
        return _selectionStart.HasValue && _selectionEnd.HasValue;
    }

    public void CopySelection()
    {
        if (HasSelection())
        {
            string selectedText = GetSelectedText();
            if (!string.IsNullOrEmpty(selectedText))
            {
                Clipboard.SetText(selectedText);
                StartCopyHighlightTransition();
            }
        }
    }

    public void PasteText(string text)
    {
        if (!string.IsNullOrEmpty(text) && !IsReadOnly)
        {
            PasteAtInputArea(text);
        }
    }

    public void PasteFromClipboard()
    {
        if (Clipboard.ContainsText() && !IsReadOnly)
        {
            string clipboardText = Clipboard.GetText();
            PasteAtInputArea(clipboardText);
        }
    }

    public void SelectAll()
    {
        if (_terminal != null)
        {
            var buffer = _terminal.Buffer;
            if (buffer.Lines.Length > 0)
            {
                _selectionStart = (0, 0);
                _selectionEnd = (buffer.Lines.Length - 1, _terminal.Cols - 1);
                UpdateSelectionHighlightRect();
            }
        }
    }
}
