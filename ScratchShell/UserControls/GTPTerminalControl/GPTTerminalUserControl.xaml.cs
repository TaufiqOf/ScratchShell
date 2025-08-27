using ScratchShell.UserControls.ThemeControl;
using ScratchShell.Services.Terminal;
using ScratchShell.Services.Navigation;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using XtermSharp;
using System.Linq;
using System.Collections.Generic;

namespace ScratchShell.UserControls.GTPTerminalControl;

public partial class GPTTerminalUserControl : UserControl, ITerminal, ITerminalDelegate
{
    private Terminal? _terminal;
    private int _cols = 80;
    private int _rows = 24;
    private double _charWidth = 8;
    private double _charHeight = 16;
    private Typeface _typeface = new Typeface("Consolas");

    private const double ExtraScrollPadding = 48; // (kept for potential future use)

    private System.Windows.Threading.DispatcherTimer _resizeRedrawTimer;
    private Size _pendingResizeSize;

    // Buffer snapshot for diff detection
    private string[]? _lastRenderedBufferLines;

    // Redraw coordination
    private volatile bool _isRedrawing = false;
    private volatile bool _redrawRequested = false;
    private readonly object _redrawLock = new object();

    // Focus state for cursor inversion
    private bool _isFocused = false;

    // Selection
    private bool _isSelecting = false;
    private readonly List<Rectangle> _selectionHighlightRects = new();
    private (int row, int col)? _selectionStart = null;
    private (int row, int col)? _selectionEnd = null;
    private bool _isCopyHighlight = false;
    private SolidColorBrush _selectionBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0, 120, 255));
    private System.Windows.Threading.DispatcherTimer? _copyHighlightTimer;

    // AutoComplete
    private ListBox? _autoCompleteListBox;
    private Popup? _autoCompletePopup;
    private bool _isAutoCompleteVisible = false;

    // Incremental rendering
    private WriteableBitmap? _surface;
    private readonly HashSet<int> _dirtyLines = new();
    private bool _fullRedrawPending = true;

    // Virtualized rendering (only last N logical lines)
    private const int MaxRenderedLines = 100;
    private int _renderStartRow = 0; // first buffer row mapped to pixel row 0

    // Track editable end including user-entered trailing spaces
    private int _currentInputEndCol = 0; // absolute column index where user input ends (can include spaces)
    private int _currentInputLineAbsRow = -1; // absolute buffer row of current input line
    private int _currentPromptEndCol = 0; // absolute column index of the prompt end

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(TerminalTheme), typeof(GPTTerminalUserControl),
        new PropertyMetadata(new TerminalTheme(), OnThemeChanged));

    public TerminalTheme Theme
    {
        get => (TerminalTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    // ITerminal interface properties
    public string InputLineSyntax { get => string.Empty; set { } }
    public bool IsReadOnly { get; set; }
    public string Text => string.Empty;

    // Events
    public event ITerminal.TerminalCommandHandler CommandEntered;
    public event ITerminal.TerminalSizeHandler TerminalSizeChanged;
    public event ITerminal.TabCompletionHandler TabCompletionRequested;

    public void RefreshTheme() => ApplyThemeProperties();

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
        Unloaded += GPTTerminalUserControl_Unloaded;

        GotFocus += (s, e) => { _isFocused = true; RedrawTerminal(); };
        LostFocus += (s, e) => { _isFocused = false; RedrawTerminal(); };

        _selectionBrush = new SolidColorBrush(Theme.SelectionColor);

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

        SizeChanged += OnControlSizeChanged;
    }

    private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            _pendingResizeSize = e.NewSize;
            _resizeRedrawTimer.Stop();
            _resizeRedrawTimer.Start();
        }
    }

    private void GPTTerminalUserControl_Loaded(object sender, RoutedEventArgs e)
    {
        Focus();
        Focusable = true;
        IsTabStop = true;
        _isFocused = true;
        if (_terminal == null)
        {
            InitializeTerminalEmulator();
        }
        UpdateTerminalLayoutAndSize();
    }

    private void GPTTerminalUserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        CleanupHighlightAnimations();
        SizeChanged -= OnControlSizeChanged;
        _resizeRedrawTimer.Stop();
    }

    private void CleanupHighlightAnimations()
    {
        if (_copyHighlightTimer != null)
        {
            _copyHighlightTimer.Stop();
            _copyHighlightTimer = null;
        }
        _isCopyHighlight = false;
        foreach (var rect in _selectionHighlightRects)
        {
            if (rect.Fill is SolidColorBrush brush)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            }
        }
    }

    private void UpdateCharSize()
    {
#pragma warning disable CS0618
        var ft = new FormattedText("W@gy", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Theme.FontFamily.Source), Theme.FontSize, Brushes.Black, new NumberSubstitution(), 1.0);
#pragma warning restore CS0618
        _charWidth = ft.WidthIncludingTrailingWhitespace / ft.Text.Length;
        _charHeight = ft.Height;
    }

    private void InitializeTerminalEmulator()
    {
        _terminal = new Terminal(this, new TerminalOptions { Cols = _cols, Rows = _rows });
    }

    public void AddOutput(string output)
    {
        if (_terminal == null) return;
        if (output.Any(c => c == '\0'))
        {
            var bytes = Encoding.Unicode.GetBytes(output);
            output = Encoding.UTF8.GetString(bytes);
        }
        var buffer = _terminal.Buffer;
        string[]? prevSnapshot = _lastRenderedBufferLines;
        _terminal.Feed(output);

        int newLineCount = buffer.Lines.Length;
        bool sizeChanged = prevSnapshot == null || prevSnapshot.Length != newLineCount;
        if (sizeChanged) _fullRedrawPending = true;

        int cols = _terminal.Cols;
        if (!_fullRedrawPending && prevSnapshot != null)
        {
            for (int row = 0; row < buffer.Lines.Length; row++)
            {
                string cur = BufferLineToString(buffer.Lines[row], cols);
                string? prev = row < prevSnapshot.Length ? prevSnapshot[row] : null;
                if (prev == null || !cur.Equals(prev)) _dirtyLines.Add(row);
            }
            if (_dirtyLines.Count > (int)(_terminal.Rows * 0.6))
            {
                _fullRedrawPending = true;
                _dirtyLines.Clear();
            }
        }
        RedrawTerminal();
        ScrollToBottom();
    }

    public void AddInput(string input)
    {
        if (IsReadOnly || _terminal == null) return;
        _terminal.Feed(input);
        var buffer = _terminal.Buffer;
        int row = buffer.Y + buffer.YBase;
        if (!_fullRedrawPending) _dirtyLines.Add(row);
        RedrawTerminal(onlyRow: row);
        ScrollToBottom();
    }

    private void ScrollToBottom() => TerminalScrollViewer.ScrollToEnd();

    private void TerminalControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsReadOnly || _terminal == null) return;

        var buffer = _terminal.Buffer;
        int absRow = buffer.Y + buffer.YBase;
        int promptEnd = GetPromptEndCol();
        // Initialize tracking if new line
        if (absRow != _currentInputLineAbsRow)
        {
            _currentInputLineAbsRow = absRow;
            _currentPromptEndCol = promptEnd;
            _currentInputEndCol = Math.Max(promptEnd, GetEditableEndCol(promptEnd));
        }
        var lineForEnd = buffer.Lines[absRow];
        // Ensure tracked end not beyond line length
        if (_currentInputEndCol > lineForEnd.Length) _currentInputEndCol = lineForEnd.Length;

        int cursorCol = buffer.X;
        bool isNavKey = e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;

        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            HandleTabCompletion();
            return;
        }

        if (_isAutoCompleteVisible && (e.Key == Key.Up || e.Key == Key.Down))
        {
            if (_autoCompleteListBox != null && _autoCompleteListBox.Items.Count > 0)
            {
                int idx = _autoCompleteListBox.SelectedIndex;
                int newIdx = e.Key == Key.Up
                    ? (idx > 0 ? idx - 1 : _autoCompleteListBox.Items.Count - 1)
                    : (idx < _autoCompleteListBox.Items.Count - 1 ? idx + 1 : 0);
                _autoCompleteListBox.SelectedIndex = newIdx;
                _autoCompleteListBox.ScrollIntoView(_autoCompleteListBox.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        // Block Up/Down outside autocomplete (prevent leaving input area/history nav for now)
        if ((e.Key == Key.Up || e.Key == Key.Down) && !_isAutoCompleteVisible)
        {
            e.Handled = true;
            return;
        }

        if (_isAutoCompleteVisible && e.Key == Key.Escape)
        {
            HideAutoComplete();
            e.Handled = true; return;
        }
        if (_isAutoCompleteVisible && e.Key == Key.Enter)
        {
            AcceptAutoCompleteSelection();
            e.Handled = true; return;
        }
        if (_isAutoCompleteVisible && !isNavKey) HideAutoComplete();

        int editableEnd = _currentInputEndCol; // tracked editable end including spaces

        // Left bound block
        if (e.Key == Key.Left && cursorCol <= promptEnd)
        {
            e.Handled = true; return;
        }
        // Right bound block
        if (e.Key == Key.Right && cursorCol >= editableEnd)
        {
            e.Handled = true; return;
        }
        // Home -> prompt start
        if (e.Key == Key.Home)
        {
            _terminal.SetCursor(promptEnd, buffer.Y);
            if (!_fullRedrawPending) _dirtyLines.Add(buffer.Y + buffer.YBase);
            RedrawTerminal(onlyRow: buffer.Y + buffer.YBase);
            e.Handled = true; return;
        }
        // Disallow typing before prompt
        if (!isNavKey && cursorCol < promptEnd)
        {
            e.Handled = true; return;
        }

        string keyToSend = string.Empty;
        if (e.Key == Key.Enter)
        {
            string inputLine = GetCurrentInputLine();
            CommandEntered?.Invoke(this, inputLine);
            keyToSend = "\r";
            // Reset tracking for next line
            _currentInputLineAbsRow = -1;
            _currentInputEndCol = 0;
        }
        else if (e.Key == Key.Back)
        {
            int editableEndForDelete = _currentInputEndCol;
            if (cursorCol > promptEnd)
            {
                var line = buffer.Lines[buffer.Y + buffer.YBase];
                int deletePos = cursorCol - 1;
                for (int i = deletePos; i < editableEndForDelete - 1 && i + 1 < line.Length; i++)
                {
                    var nextCell = line[i + 1];
                    line[i] = nextCell; // shift left including spaces
                }
                // Blank out last cell of the edited region
                int lastPos = Math.Min(editableEndForDelete - 1, line.Length - 1);
                if (lastPos >= promptEnd)
                {
                    var blank = line[lastPos]; blank.Code = 0; line[lastPos] = blank;
                }
                _terminal.SetCursor(deletePos, buffer.Y);
                // Adjust tracked end if deleting at end
                if (deletePos + 1 == _currentInputEndCol)
                {
                    _currentInputEndCol = Math.Max(promptEnd, _currentInputEndCol - 1);
                }
                int lastRow = buffer.Y + buffer.YBase;
                if (!_fullRedrawPending) _dirtyLines.Add(lastRow);
                RedrawTerminal(onlyRow: lastRow);
            }
            e.Handled = true; return;
        }
        else
        {
            switch (e.Key)
            {
                case Key.Delete: keyToSend = "\u001B[3~"; break;
                case Key.Escape: keyToSend = "\u001B"; break;
                case Key.Right: keyToSend = "\u001B[C"; break; // allowed only if < editableEnd
                case Key.Left: keyToSend = "\u001B[D"; break;
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
                        e.Handled = true; return;
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
                        {
                            if (cursorCol < editableEnd) // insertion (shift remainder right)
                            {
                                var line = buffer.Lines[buffer.Y + buffer.YBase];
                                var remSb = new StringBuilder();
                                for (int col = cursorCol; col < editableEnd && col < line.Length; col++)
                                {
                                    var cell = line[col];
                                    remSb.Append(cell.Code != 0 ? (char)cell.Code : ' ');
                                }
                                string remainder = remSb.ToString();
                                if (remainder.Length > 0)
                                {
                                    string esc = "\u001B";
                                    string seq = c + remainder + esc + "[" + remainder.Length + "D";
                                    _terminal.Feed(seq);
                                    // cursor stays at insert position +1; adjust end
                                    _currentInputEndCol += 1;
                                    int lastRow2 = buffer.Y + buffer.YBase;
                                    if (!_fullRedrawPending) _dirtyLines.Add(lastRow2);
                                    RedrawTerminal(onlyRow: lastRow2);
                                    e.Handled = true; return;
                                }
                            }
                            // At end: append and advance end
                            keyToSend = c.ToString();
                            _currentInputEndCol = Math.Max(_currentInputEndCol + 1, buffer.X + 1);
                        }
                    }
                    break;
            }
        }

        if (!string.IsNullOrEmpty(keyToSend))
        {
            _terminal.Feed(keyToSend);
            int lastRow = buffer.Y + buffer.YBase;
            if (!_fullRedrawPending) _dirtyLines.Add(lastRow);
            RedrawTerminal(onlyRow: lastRow);
        }
        e.Handled = true;
    }

    private void HandleTabCompletion()
    {
        if (_terminal == null) return;
        var args = new TabCompletionEventArgs
        {
            CurrentLine = GetCurrentInputLine(),
            CursorPosition = GetCursorPosition(),
            WorkingDirectory = "~"
        };
        TabCompletionRequested?.Invoke(this, args);
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
            if (c == '$' || c == '#') lastPrompt = i;
        }
        return lastPrompt + 1;
    }

    public string GetCurrentInputLine()
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
        int promptIdx = Math.Max(text.LastIndexOf('$'), text.LastIndexOf('#'));
        if (promptIdx >= 0)
        {
            return promptIdx + 1 < text.Length ? text[(promptIdx + 1)..].Trim() : string.Empty;
        }
        return text;
    }

    private void TerminalControl_MouseDown(object sender, MouseButtonEventArgs e)
    {
        TerminalCanvas.Focus();
        _isFocused = true;

        bool plainLeft = e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.None;
        if (plainLeft)
        {
            if (_isCopyHighlight) CleanupHighlightAnimations();
            // Clear any previous highlight BEFORE setting new selection
            foreach (var rect in _selectionHighlightRects)
                TerminalCanvas.Children.Remove(rect);
            _selectionHighlightRects.Clear();
            _selectionStart = null;
            _selectionEnd = null;

            if (_terminal != null)
            {
                var pos = e.GetPosition(TerminalCanvas);
                int visualCol = (int)(pos.X / _charWidth);
                int visualRow = (int)(pos.Y / _charHeight);
                var buffer = _terminal.Buffer;
                int logicalRow = _renderStartRow + visualRow;
                if (logicalRow < 0) logicalRow = 0;
                if (logicalRow >= buffer.Lines.Length) logicalRow = buffer.Lines.Length - 1;
                int maxCol = _terminal.Cols - 1;
                int logicalCol = Math.Clamp(visualCol, 0, maxCol);
                _selectionStart = (logicalRow, logicalCol);
                _selectionEnd = (logicalRow, logicalCol);
                _isSelecting = true;
                UpdateSelectionHighlightRect();
                TerminalCanvas.CaptureMouse();
            }
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.Control)
        {
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
            if (Clipboard.ContainsText())
            {
                PasteAtInputArea(Clipboard.GetText());
            }
            e.Handled = true;
        }
    }

    private void StartCopyHighlightTransition()
    {
        if (_copyHighlightTimer != null)
        {
            _copyHighlightTimer.Stop();
            _copyHighlightTimer = null;
        }
        _isCopyHighlight = true;
        if (_selectionHighlightRects.Count == 0)
        {
            _isCopyHighlight = false; return;
        }
        var defaultSelectionColor = Theme.SelectionColor;
        var copySelectionColor = Theme.CopySelectionColor;
        var animationsToDefault = new List<ColorAnimation>();
        foreach (var rect in _selectionHighlightRects)
        {
            if (rect.Fill is SolidColorBrush brush)
            {
                var animToCopy = new ColorAnimation
                {
                    From = defaultSelectionColor,
                    To = copySelectionColor,
                    Duration = TimeSpan.FromMilliseconds(400)
                };
                var animBack = new ColorAnimation
                {
                    From = copySelectionColor,
                    To = defaultSelectionColor,
                    Duration = TimeSpan.FromMilliseconds(600)
                };
                animationsToDefault.Add(animBack);
                brush.BeginAnimation(SolidColorBrush.ColorProperty, animToCopy);
            }
        }
        _copyHighlightTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _copyHighlightTimer.Tick += (s, e) =>
        {
            _copyHighlightTimer.Stop();
            _copyHighlightTimer = null;
            for (int i = 0; i < _selectionHighlightRects.Count && i < animationsToDefault.Count; i++)
            {
                var rect = _selectionHighlightRects[i];
                if (rect.Fill is SolidColorBrush brush)
                {
                    var back = animationsToDefault[i];
                    if (i == _selectionHighlightRects.Count - 1)
                    {
                        back.Completed += (s2, e2) =>
                        {
                            _isCopyHighlight = false;
                            foreach (var r in _selectionHighlightRects)
                            {
                                if (r.Fill is SolidColorBrush b)
                                {
                                    b.Color = Theme.SelectionColor;
                                    b.BeginAnimation(SolidColorBrush.ColorProperty, null);
                                }
                            }
                        };
                    }
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, back);
                }
            }
        };
        _copyHighlightTimer.Start();
    }

    private void TerminalCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(TerminalCanvas);
            int col = (int)(pos.X / _charWidth);
            int row = (int)(pos.Y / _charHeight) + _renderStartRow; // map visual to logical
            if (_isSelecting)
            {
                _selectionEnd = (row, col);
                UpdateSelectionHighlightRect();
            }
            else
            {
                _isSelecting = true;
                _selectionStart = (row, col);
                _selectionEnd = (row, col);
            }
        }
    }

    private void TerminalCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelecting && e.ChangedButton == MouseButton.Left)
        {
            _isSelecting = false;
            if (TerminalCanvas.IsMouseCaptured) TerminalCanvas.ReleaseMouseCapture();
            UpdateSelectionHighlightRect();
        }
    }

    private void UpdateSelectionHighlightRect()
    {
        if (_isCopyHighlight) return;
        foreach (var r in _selectionHighlightRects) TerminalCanvas.Children.Remove(r);
        _selectionHighlightRects.Clear();
        if (_copyHighlightTimer != null) { _copyHighlightTimer.Stop(); _copyHighlightTimer = null; }
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue || _terminal == null) return;

        var (row1, col1) = _selectionStart.Value;
        var (row2, col2) = _selectionEnd.Value;
        int startRow = Math.Min(row1, row2);
        int endRow = Math.Max(row1, row2);
        int startCol = Math.Min(col1, col2);
        int endCol = Math.Max(col1, col2);
        int maxCol = _terminal.Cols - 1;

        for (int row = startRow; row <= endRow; row++)
        {
            if (row < _renderStartRow) continue; // not rendered
            int visualRow = row - _renderStartRow;
            int colStart = (row == startRow) ? startCol : 0;
            int colEnd = (row == endRow) ? endCol : maxCol;
            colStart = Math.Clamp(colStart, 0, maxCol);
            colEnd = Math.Clamp(colEnd, 0, maxCol);
            if (colEnd < colStart) continue;
            var rect = new Rectangle
            {
                Width = (colEnd - colStart + 1) * _charWidth,
                Height = _charHeight,
                Fill = new SolidColorBrush(Theme.SelectionColor),
                Opacity = 0.5,
                IsHitTestVisible = false
            };
            TerminalCanvas.Children.Add(rect);
            Canvas.SetLeft(rect, colStart * _charWidth);
            Canvas.SetTop(rect, visualRow * _charHeight);
            _selectionHighlightRects.Add(rect);
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
            if (row < 0 || row >= buffer.Lines.Length) continue;
            var line = buffer.Lines[row];
            int colStart = (row == startRow) ? startCol : 0;
            int colEnd = (row == endRow) ? endCol : _terminal.Cols - 1;
            var lineSb = new StringBuilder();
            for (int c = colStart; c <= colEnd && c < line.Length; c++)
            {
                var cell = line[c];
                lineSb.Append(cell.Code != 0 ? (char)cell.Code : ' ');
            }
            sb.Append(lineSb.ToString().TrimEnd());
            if (row < endRow) sb.AppendLine();
        }
        return sb.ToString();
    }

    private void PasteAtInputArea(string text)
    {
        if (_terminal == null || IsReadOnly) return;
        var buffer = _terminal.Buffer;
        int row = buffer.Y + buffer.YBase;
        int col = buffer.X;
        var line = buffer.Lines[row];
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
        if (!_fullRedrawPending) _dirtyLines.Add(row);
        RedrawTerminal(onlyRow: row);
    }

    public void ApplyThemeProperties()
    {
        _typeface = new Typeface(Theme.FontFamily.Source);
        _selectionBrush = new SolidColorBrush(Theme.SelectionColor);
        if (TerminalGrid != null)
            TerminalGrid.Background = Theme.Background;
        UpdateCharSize();
        _fullRedrawPending = true;
        _dirtyLines.Clear();
        RedrawTerminal();
        _selectionStart = null;
        _selectionEnd = null;
    }

    private Size _lastLayoutSize = Size.Empty;
    private int _lastCols = -1;
    private int _lastRows = -1;

    private void UpdateTerminalLayoutAndSize(Size? newSize = null)
    {
        UpdateCharSize();
        Size size = newSize ?? new Size(ActualWidth, ActualHeight);
        int cols = Math.Max(10, (int)(size.Width / _charWidth));
        int rows = Math.Max(2, (int)(size.Height / _charHeight));
        TerminalCanvas.Width = cols * _charWidth;
        TerminalCanvas.Height = rows * _charHeight;
        if (_terminal != null && (size != _lastLayoutSize || cols != _lastCols || rows != _lastRows))
        {
            _terminal.Resize(cols, rows);
            _lastLayoutSize = size;
            _lastCols = cols;
            _lastRows = rows;
            _fullRedrawPending = true;
            _dirtyLines.Clear();
        }
        TerminalSizeChanged?.Invoke(this, size);
        RedrawTerminal();
        UpdateSelectionHighlightRect();
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

    private static string BufferLineToString(dynamic line, int cols)
    {
        var sb = new StringBuilder(cols);
        for (int i = 0; i < cols; i++)
        {
            if (i < line.Length)
            {
                var cell = line[i];
                char ch = cell.Code != 0 ? (char)cell.Code : ' ';
                sb.Append(ch);
            }
            else sb.Append(' ');
        }
        return sb.ToString();
    }

    private void RedrawTerminal(int? onlyRow = null)
    {
        if (_terminal == null) return;
        if (onlyRow.HasValue && !_fullRedrawPending) _dirtyLines.Add(onlyRow.Value);
        else if (!onlyRow.HasValue && _dirtyLines.Count == 0 && !_fullRedrawPending) _fullRedrawPending = true;

        lock (_redrawLock)
        {
            if (_isRedrawing)
            {
                _redrawRequested = true;
                return;
            }
            _isRedrawing = true;
            _redrawRequested = false;
        }
        PerformRedraw();
    }

    private void PerformRedraw()
    {
        try
        {
            if (_terminal == null)
            {
                CompleteRedraw();
                return;
            }
            var buffer = _terminal.Buffer;
            int cols = _terminal.Cols;
            int totalRows = buffer.Lines.Length;
            if (totalRows <= 0)
            {
                CompleteRedraw();
                return;
            }

            int previousRenderStart = _renderStartRow;
            // Virtualization window
            _renderStartRow = totalRows > MaxRenderedLines ? totalRows - MaxRenderedLines : 0;
            int renderRowCount = totalRows - _renderStartRow;

            double cw = _charWidth;
            double ch = _charHeight;
            int linePixelHeight = (int)Math.Ceiling(ch);
            if (linePixelHeight <= 0) linePixelHeight = 1;
            int pixelWidth = (int)Math.Ceiling(cols * cw);
            if (pixelWidth <= 0) pixelWidth = 1;
            int pixelHeight = linePixelHeight * renderRowCount;

            bool allocate = _surface == null || _surface.PixelWidth != pixelWidth || _surface.PixelHeight != pixelHeight;
            if (allocate)
            {
                _surface = new WriteableBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32, null);
                _fullRedrawPending = true;
                _dirtyLines.Clear();
            }

            IEnumerable<int> linesToDraw = _fullRedrawPending
                ? Enumerable.Range(_renderStartRow, renderRowCount)
                : _dirtyLines.Where(r => r >= _renderStartRow).OrderBy(r => r).ToArray();

            if (!linesToDraw.Any()) { CompleteRedraw(); return; }

            var theme = Theme;
            var fgDefault = theme.Foreground as SolidColorBrush ?? Brushes.LightGray;
            var bgDefault = theme.Background as SolidColorBrush ?? Brushes.Black;
            var cursorBrush = (theme.CursorColor as SolidColorBrush) ?? fgDefault;

            if (_lastRenderedBufferLines == null || _lastRenderedBufferLines.Length != totalRows)
            {
                _lastRenderedBufferLines = new string[totalRows];
            }

            foreach (var row in linesToDraw)
            {
                if (row < 0 || row >= buffer.Lines.Length) continue;
                int visualRow = row - _renderStartRow;
                if (visualRow < 0 || visualRow >= renderRowCount) continue;
                int destY = visualRow * linePixelHeight;
                if (destY + linePixelHeight > _surface.PixelHeight) continue;
                var line = buffer.Lines[row];
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(bgDefault, null, new Rect(0, 0, pixelWidth, linePixelHeight));
                    for (int col = 0; col < cols; col++)
                    {
                        char chChar = ' ';
                        int attr = XtermSharp.CharData.DefaultAttr;
                        if (col < line.Length)
                        {
                            var cell = line[col];
                            chChar = cell.Code != 0 ? (char)cell.Code : ' ';
                            attr = cell.Attribute;
                        }
                        bool isCursor = (row == buffer.Y + buffer.YBase && col == buffer.X);
                        bool inverse = IsInverse(attr) ^ (isCursor && _isFocused);
                        Brush fg = attr == XtermSharp.CharData.DefaultAttr ? fgDefault : GetAnsiForeground(attr, inverse);
                        Brush bg = bgDefault;
                        dc.DrawRectangle(bg, null, new Rect(col * cw, 0, cw, linePixelHeight));
#pragma warning disable CS0618
                        var ft = new FormattedText(chChar.ToString(), System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface(theme.FontFamily, FontStyles.Normal, IsBold(attr) ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                            theme.FontSize, fg, VisualTreeHelper.GetDpi(this).PixelsPerDip);
#pragma warning restore CS0618
                        dc.DrawText(ft, new Point(col * cw, 0));
                        if (isCursor && _isFocused)
                        {
                            dc.DrawRectangle(cursorBrush, null, new Rect(col * cw, 0, cw, linePixelHeight));
#pragma warning disable CS0618
                            var ftC = new FormattedText(chChar.ToString(), System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                new Typeface(theme.FontFamily, FontStyles.Normal, IsBold(attr) ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                                theme.FontSize, bgDefault, VisualTreeHelper.GetDpi(this).PixelsPerDip);
#pragma warning restore CS0618
                            dc.DrawText(ftC, new Point(col * cw, 0));
                        }
                    }
                }
                var lineBmp = new RenderTargetBitmap(pixelWidth, linePixelHeight, 96, 96, PixelFormats.Pbgra32);
                lineBmp.Render(dv);
                int stride = pixelWidth * 4;
                var pixels = new byte[stride * linePixelHeight];
                lineBmp.CopyPixels(pixels, stride, 0);
                _surface!.WritePixels(new Int32Rect(0, destY, pixelWidth, linePixelHeight), pixels, stride, 0);
                _lastRenderedBufferLines[row] = BufferLineToString(line, cols);
            }

            _dirtyLines.Clear();
            _fullRedrawPending = false;

            TerminalBitmapImage.Source = _surface;
            TerminalBitmapImage.Width = pixelWidth;
            TerminalBitmapImage.Height = pixelHeight;
            TerminalCanvas.Width = pixelWidth;
            TerminalCanvas.Height = renderRowCount * ch;
            if (Cursor != null) Cursor.Visibility = Visibility.Collapsed;

            // If virtualization window moved, refresh selection highlight
            if (previousRenderStart != _renderStartRow && HasSelection())
            {
                UpdateSelectionHighlightRect();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Incremental redraw exception: {ex}");
        }
        finally
        {
            CompleteRedraw();
        }
    }

    private void CompleteRedraw()
    {
        bool again = false;
        lock (_redrawLock)
        {
            _isRedrawing = false;
            again = _redrawRequested;
            _redrawRequested = false;
        }
        if (again) RedrawTerminal();
    }

    private static char? KeyToChar(Key key, ModifierKeys modifiers)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            char c = (char)('a' + (key - Key.A));
            if ((modifiers & ModifierKeys.Shift) != 0) c = char.ToUpper(c);
            return c;
        }
        if (key >= Key.D0 && key <= Key.D9)
        {
            char c = (char)('0' + (key - Key.D0));
            if ((modifiers & ModifierKeys.Shift) != 0)
            {
                string shifted = ")!@#$%^&*(";
                c = shifted[key - Key.D0];
            }
            return c;
        }
        if (key == Key.Space) return ' ';
        if (key >= Key.Oem1 && key <= Key.OemBackslash)
        {
            bool shift = (modifiers & ModifierKeys.Shift) != 0;
            return key switch
            {
                Key.Oem1 => shift ? ':' : ';',
                Key.OemPlus => shift ? '+' : '=',
                Key.OemComma => shift ? '<' : ',',
                Key.OemMinus => shift ? '_' : '-',
                Key.OemPeriod => shift ? '>' : '.',
                Key.Oem2 => shift ? '?' : '/',
                Key.Oem3 => shift ? '~' : '`',
                Key.Oem4 => shift ? '{' : '[',
                Key.Oem5 => shift ? '|' : '\\',
                Key.Oem6 => shift ? '}' : ']',
                Key.Oem7 => shift ? '"' : '\'',
                _ => (char?)null
            };
        }
        return null;
    }

    // ITerminalDelegate minimal implementations
    public void ShowCursor(Terminal terminal) { }
    public void SetTerminalTitle(Terminal terminal, string title) { }
    public void SetTerminalIconTitle(Terminal terminal, string title) { }
    void ITerminalDelegate.SizeChanged(Terminal terminal) { }
    public void Send(byte[] data) { }
    public string WindowCommand(Terminal terminal, WindowManipulationCommand cmd, params int[] args) => string.Empty;
    public bool IsProcessTrusted() => true;

    // Public ITerminal copy/paste API
    public bool HasSelection() => _selectionStart.HasValue && _selectionEnd.HasValue;

    public void CopySelection()
    {
        if (HasSelection())
        {
            var text = GetSelectedText();
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
                StartCopyHighlightTransition();
            }
        }
    }

    public void PasteText(string text) => PasteAtInputArea(text);

    public void PasteFromClipboard()
    {
        if (Clipboard.ContainsText()) PasteAtInputArea(Clipboard.GetText());
    }

    public void SelectAll()
    {
        if (_terminal == null) return;
        var buffer = _terminal.Buffer;
        if (buffer.Lines.Length == 0) return;
        _selectionStart = (0, 0);
        _selectionEnd = (buffer.Lines.Length - 1, _terminal.Cols - 1);
        UpdateSelectionHighlightRect();
    }

    public new void Focus() => TerminalCanvas.Focus();

    // AutoComplete
    public void ShowAutoCompleteResults(AutoCompleteResult result)
    {
        if (result == null || !result.HasSuggestions)
        {
            HideAutoComplete();
            return;
        }
        CreateAutoCompletePopupIfNeeded();
        if (_autoCompleteListBox != null && _autoCompletePopup != null)
        {
            _autoCompleteListBox.ItemsSource = result.Suggestions.Select(s => new AutoCompleteItem
            {
                Text = s.Text,
                DisplayText = s.DisplayText,
                Description = s.Description,
                Type = s.Type
            }).ToList();

            var (cx, cy) = GetCursorScreenPosition();
            _autoCompletePopup.HorizontalOffset = cx;
            _autoCompletePopup.VerticalOffset = cy + _charHeight;
            _autoCompletePopup.IsOpen = true;
            _isAutoCompleteVisible = true;
            if (_autoCompleteListBox.Items.Count > 0)
            {
                _autoCompleteListBox.SelectedIndex = 0;
                _autoCompleteListBox.ScrollIntoView(_autoCompleteListBox.SelectedItem);
            }
        }
    }

    public void HideAutoComplete()
    {
        if (_autoCompletePopup != null) _autoCompletePopup.IsOpen = false;
        _isAutoCompleteVisible = false;
    }

    public int GetCursorPosition()
    {
        if (_terminal == null) return 0;
        var buffer = _terminal.Buffer;
        int promptEnd = GetPromptEndCol();
        return Math.Max(0, buffer.X - promptEnd);
    }

    private void CreateAutoCompletePopupIfNeeded()
    {
        if (_autoCompletePopup != null) return;
        _autoCompleteListBox = new ListBox
        {
            MaxHeight = 200,
            MinWidth = 300,
            Background = Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1)
        };
        KeyboardNavigation.SetDirectionalNavigation(_autoCompleteListBox, KeyboardNavigationMode.Cycle);
        var dataTemplate = new DataTemplate();
        var spFactory = new FrameworkElementFactory(typeof(StackPanel));
        spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding("DisplayText"));
        textFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        textFactory.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 10, 0));
        var descFactory = new FrameworkElementFactory(typeof(TextBlock));
        descFactory.SetBinding(TextBlock.TextProperty, new Binding("Description"));
        descFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
        descFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
        spFactory.AppendChild(textFactory);
        spFactory.AppendChild(descFactory);
        dataTemplate.VisualTree = spFactory;
        _autoCompleteListBox.ItemTemplate = dataTemplate;
        _autoCompleteListBox.PreviewKeyDown += AutoCompleteListBox_PreviewKeyDown;
        _autoCompleteListBox.MouseDoubleClick += (s, e) => AcceptAutoCompleteSelection();
        _autoCompletePopup = new Popup
        {
            Child = _autoCompleteListBox,
            PlacementTarget = this,
            Placement = PlacementMode.Relative,
            StaysOpen = false,
            AllowsTransparency = true,
            Focusable = false
        };
        _autoCompletePopup.Closed += (s, e) => _isAutoCompleteVisible = false;
    }

    private void AutoCompleteListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            AcceptAutoCompleteSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideAutoComplete();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_autoCompleteListBox != null && _autoCompleteListBox.Items.Count > 0)
            {
                int idx = _autoCompleteListBox.SelectedIndex;
                int newIdx = idx > 0 ? idx - 1 : _autoCompleteListBox.Items.Count - 1;
                _autoCompleteListBox.SelectedIndex = newIdx;
                _autoCompleteListBox.ScrollIntoView(_autoCompleteListBox.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (_autoCompleteListBox != null && _autoCompleteListBox.Items.Count > 0)
            {
                int idx = _autoCompleteListBox.SelectedIndex;
                int newIdx = idx < _autoCompleteListBox.Items.Count - 1 ? idx + 1 : 0;
                _autoCompleteListBox.SelectedIndex = newIdx;
                _autoCompleteListBox.ScrollIntoView(_autoCompleteListBox.SelectedItem);
            }
            e.Handled = true;
        }
    }

    private void AcceptAutoCompleteSelection()
    {
        if (_autoCompleteListBox?.SelectedItem is AutoCompleteItem item)
        {
            string current = GetCurrentInputLine();
            var words = current.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (words.Count > 0)
            {
                words[^1] = item.Text;
                string newLine = string.Join(" ", words);
                AddInput(newLine);
            }
        }
        HideAutoComplete();
        Focus();
    }

    private void ClearCurrentInput()
    {
        if (_terminal == null) return;
        var buffer = _terminal.Buffer;
        int promptEnd = GetPromptEndCol();
        var line = buffer.Lines[buffer.Y + buffer.YBase];
        for (int i = promptEnd; i < line.Length; i++)
        {
            var cell = line[i]; cell.Code = 0; line[i] = cell;
        }
        _terminal.SetCursor(promptEnd, buffer.Y);
        RedrawTerminal(onlyRow: buffer.Y + buffer.YBase);
    }

    private (double x, double y) GetCursorScreenPosition()
    {
        if (_terminal == null) return (0, 0);
        var buffer = _terminal.Buffer;
        int absRow = buffer.Y + buffer.YBase;
        int visualRow = absRow - _renderStartRow; // map to rendered row
        double x = buffer.X * _charWidth;
        double y = visualRow * _charHeight;
        var pt = TerminalCanvas.TranslatePoint(new Point(x, y), this);
        return (pt.X, pt.Y);
    }

    private int GetEditableEndCol(int promptEnd)
    {
        if (_terminal == null) return promptEnd;
        if (_currentInputLineAbsRow == (_terminal.Buffer.Y + _terminal.Buffer.YBase) && _currentInputEndCol > 0)
            return _currentInputEndCol; // use tracked value
        var buffer = _terminal.Buffer;
        var line = buffer.Lines[buffer.Y + buffer.YBase];
        int last = -1;
        for (int i = promptEnd; i < line.Length; i++)
        {
            var cell = line[i];
            char c = cell.Code == 0 ? ' ' : (char)cell.Code;
            if (c != ' ') last = i;
        }
        return Math.Max(promptEnd, last + 1);
    }

    private class AutoCompleteItem
    {
        public string Text { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CompletionType Type { get; set; }
    }
}