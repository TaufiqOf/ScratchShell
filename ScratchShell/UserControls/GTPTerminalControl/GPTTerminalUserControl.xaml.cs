using ScratchShell.UserControls.ThemeControl;
using ScratchShell.Services.Terminal;
using ScratchShell.Services.Navigation;
using System.Diagnostics;
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
using System.ComponentModel;
using Wpf.Ui.Extensions;

namespace ScratchShell.UserControls.GTPTerminalControl;

public partial class GPTTerminalUserControl : UserControl, ITerminal, ITerminalDelegate
{
    private Terminal? _terminal;
    private int _cols = 80;
    private int _rows = 24;
    private double _charWidth = 8;
    private double _charHeight = 16;
    private Typeface _typeface = new Typeface("Consolas");
    private bool _initialized = false;

    private System.Windows.Threading.DispatcherTimer _resizeRedrawTimer;
    private Size _pendingResizeSize;

    private string[]? _lastRenderedBufferLines; // snapshot

    private volatile bool _isRedrawing = false;
    private volatile bool _redrawRequested = false;
    private readonly object _redrawLock = new();

    private bool _isFocused = false;

    // Selection
    private bool _isSelecting = false;
    private readonly List<Rectangle> _selectionHighlightRects = new();
    private (int row, int col)? _selectionStart = null;
    private (int row, int col)? _selectionEnd = null;
    private bool _isCopyHighlight = false;
    private SolidColorBrush _selectionBrush = new(System.Windows.Media.Color.FromArgb(80, 0, 120, 255));
    private System.Windows.Threading.DispatcherTimer? _copyHighlightTimer;

    // Autocomplete
    private ListBox? _autoCompleteListBox;
    private Popup? _autoCompletePopup;
    private bool _isAutoCompleteVisible = false;
    private System.Windows.Threading.DispatcherTimer? _autoCompleteRefreshTimer; // debounce
    private bool _autoCompleteRefreshPending = false;

    // Incremental rendering surface
    private WriteableBitmap? _surface;
    private readonly HashSet<int> _dirtyLines = new();
    private bool _fullRedrawPending = true;

    // Virtualization
    private const int MaxRenderedLines = 100;
    private int _renderStartRow = 0;

    // Input tracking
    private int _currentInputEndCol = 0;
    private int _currentInputLineAbsRow = -1;
    private int _currentPromptEndCol = 0;
    private string? _lastSubmittedCommand = null;
    // Line height in device-independent units (DIPs) for layout/interaction
    private double _lineHeightDip = 0;
    // Line height in device pixels (for bitmap math) - kept for compatibility with existing code paths
    private int _linePixelHeight = 0;

    // Cached ANSI foreground brushes derived from Theme.AnsiForegroundPalette
    private Brush[] _ansiForegroundBrushes = TerminalTheme.DefaultAnsiForegroundPalette
        .Select(c => (Brush)new SolidColorBrush(c))
        .ToArray();

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(TerminalTheme), typeof(GPTTerminalUserControl),
        new PropertyMetadata(new TerminalTheme(), OnThemeChanged));

    public TerminalTheme Theme
    {
        get => (TerminalTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public string InputLineSyntax { get => string.Empty; set { } }
    public bool IsReadOnly { get; set; }
    public string Text => string.Empty; // placeholder

    public event ITerminal.TerminalCommandHandler? CommandEntered;
    public event ITerminal.TerminalSizeHandler? TerminalSizeChanged;
    public event ITerminal.TabCompletionHandler? TabCompletionRequested;

    public void RefreshTheme() => ApplyThemeProperties();

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GPTTerminalUserControl ctrl)
        {
            if (e.OldValue is TerminalTheme oldTheme)
            {
                oldTheme.PropertyChanged -= ctrl.Theme_PropertyChanged;
            }
            if (e.NewValue is TerminalTheme newTheme)
            {
                newTheme.PropertyChanged += ctrl.Theme_PropertyChanged;
            }
            ctrl.ApplyThemeProperties();
        }
    }

    private void Theme_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalTheme.AnsiForegroundPalette))
        {
            BuildAnsiBrushes();
            _fullRedrawPending = true; _dirtyLines.Clear();
            RedrawTerminal();
            return;
        }
        if (e.PropertyName is nameof(TerminalTheme.FontFamily)
            or nameof(TerminalTheme.FontSize)
            or nameof(TerminalTheme.Foreground)
            or nameof(TerminalTheme.Background)
            or nameof(TerminalTheme.SelectionColor)
            or nameof(TerminalTheme.CursorColor)
            or nameof(TerminalTheme.CopySelectionColor))
        {
            ApplyThemeProperties();
        }
    }

    public GPTTerminalUserControl()
    {
        InitializeComponent();
        Loaded += GPTTerminalUserControlLoaded;
        Unloaded += GPTTerminalUserControlUnloaded;

        //GotFocus += (s, e) => { _isFocused = true; RedrawTerminal(); };
        //LostFocus += (s, e) => { _isFocused = false; RedrawTerminal(); };

        _selectionBrush = new SolidColorBrush(Theme.SelectionColor);
        BuildAnsiBrushes();
        if (Theme != null)
        {
            Theme.PropertyChanged += Theme_PropertyChanged;
        }

        _resizeRedrawTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _resizeRedrawTimer.Tick += (s, e) =>
        {
            _resizeRedrawTimer.Stop();
            if (_terminal != null)
                UpdateTerminalLayoutAndSize(_pendingResizeSize);
        };

        SizeChanged += OnControlSizeChanged;
    }

    private void BuildAnsiBrushes()
    {
        var pal = Theme?.AnsiForegroundPalette;
        if (pal == null || pal.Count < 16)
        {
            pal = TerminalTheme.DefaultAnsiForegroundPalette;
        }
        _ansiForegroundBrushes = pal.Take(16)
            .Select(c => (Brush)new SolidColorBrush(c))
            .ToArray();
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

    private void GPTTerminalUserControlLoaded(object sender, RoutedEventArgs e)
    {
        Focus();
        Focusable = true;
        IsTabStop = true;
        _isFocused = true;
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        if (_terminal == null)
            InitializeTerminalEmulator();
        UpdateTerminalLayoutAndSize();
        // Track scroll to reposition autocomplete popup
        if (TerminalScrollViewer != null)
            TerminalScrollViewer.ScrollChanged += TerminalScrollViewerScrollChanged;
    }

    private void GPTTerminalUserControlUnloaded(object sender, RoutedEventArgs e)
    {
        CleanupHighlightAnimations();
        SizeChanged -= OnControlSizeChanged;
        _resizeRedrawTimer.Stop();
        if (TerminalScrollViewer != null)
            TerminalScrollViewer.ScrollChanged -= TerminalScrollViewerScrollChanged;
        if (Theme != null)
            Theme.PropertyChanged -= Theme_PropertyChanged;
    }

    private void TerminalScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isAutoCompleteVisible)
            UpdateAutoCompletePopupPosition();
    }

    private void CleanupHighlightAnimations()
    {
        _copyHighlightTimer?.Stop();
        _copyHighlightTimer = null;
        _isCopyHighlight = false;
        foreach (var rect in _selectionHighlightRects)
            if (rect.Fill is SolidColorBrush b)
                b.BeginAnimation(SolidColorBrush.ColorProperty, null);
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
        _startRender = true;

        // Remote echo suppression: if the first line of output exactly matches the last submitted command, remove it
        if (!string.IsNullOrEmpty(_lastSubmittedCommand))
        {
            var span = output.AsSpan();
            int newlineIdx = span.IndexOf('\n');
            ReadOnlySpan<char> firstLine = newlineIdx >= 0 ? span.Slice(0, newlineIdx).TrimEnd('\r') : span.TrimEnd('\r');
            if (firstLine.SequenceEqual(_lastSubmittedCommand.AsSpan()))
            {
                // Skip that first line and following newline
                if (newlineIdx >= 0)
                {
                    output = "\n\r";
                }
                else
                {
                    output = string.Empty;
                }
                _lastSubmittedCommand = null; // consumed
            }
        }
        if (output.Length == 0) return;
        if (output.Any(c => c == '\0'))
        {
            var bytes = Encoding.Unicode.GetBytes(output);
            output = Encoding.UTF8.GetString(bytes);
        }
        var buffer = _terminal.Buffer;
        var prevSnapshot = _lastRenderedBufferLines;
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

        // Ensure tracking is initialized for current line before inserting
        var buffer = _terminal.Buffer;
        int absRowBefore = buffer.Y + buffer.YBase;
        int promptEndBefore = GetPromptEndCol();
        if (_currentInputLineAbsRow != absRowBefore)
        {
            _currentInputLineAbsRow = absRowBefore;
            _currentPromptEndCol = promptEndBefore;
            _currentInputEndCol = Math.Max(promptEndBefore, GetEditableEndCol(promptEndBefore));
        }

        // Feed the text (simulates user typing)
        _terminal.Feed(input);

        // Refresh buffer references after feed (cursor may have moved)
        buffer = _terminal.Buffer;
        int absRowAfter = buffer.Y + buffer.YBase;
        if (absRowAfter == _currentInputLineAbsRow)
        {
            // Same line: extend editable end to new cursor position
            _currentInputEndCol = Math.Max(_currentInputEndCol, buffer.X);
        }
        else
        {
            // Moved to a new line (e.g., newline in input) – reset tracking for new line
            _currentInputLineAbsRow = absRowAfter;
            _currentPromptEndCol = GetPromptEndCol();
            _currentInputEndCol = Math.Max(_currentPromptEndCol, GetEditableEndCol(_currentPromptEndCol));
        }

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
                int newIdx = e.Key == Key.Up ? (idx > 0 ? idx - 1 : _autoCompleteListBox.Items.Count - 1) : (idx < _autoCompleteListBox.Items.Count - 1 ? idx + 1 : 0);
                _autoCompleteListBox.SelectedIndex = newIdx;
                _autoCompleteListBox.ScrollIntoView(_autoCompleteListBox.SelectedItem);
            }
            e.Handled = true; return;
        }

        // Block Up/Down outside autocomplete (prevent leaving input area/history nav for now)
        if ((e.Key == Key.Up || e.Key == Key.Down) && !_isAutoCompleteVisible)
        {
            e.Handled = true; return;
        }

        if (_isAutoCompleteVisible && e.Key == Key.Escape)
        {
            HideAutoComplete(); e.Handled = true; return;
        }
        if (_isAutoCompleteVisible && e.Key == Key.Enter)
        {
            AcceptAutoCompleteSelection(); e.Handled = true; return;
        }
        // NOTE: Removed auto-hide on typing; keep popup open and refresh instead.

        int editableEnd = _currentInputEndCol; // tracked editable end including spaces

        // Left bound block
        if (e.Key == Key.Left && cursorCol <= promptEnd) { e.Handled = true; return; }
        // Right bound block
        if (e.Key == Key.Right && cursorCol >= editableEnd) { e.Handled = true; return; }
        // Home -> prompt start
        if (e.Key == Key.Home)
        {
            _terminal.SetCursor(promptEnd, buffer.Y);
            if (!_fullRedrawPending) _dirtyLines.Add(buffer.Y + buffer.YBase);
            RedrawTerminal(onlyRow: buffer.Y + buffer.YBase);
            e.Handled = true; return;
        }
        // Disallow typing before prompt
        if (!isNavKey && cursorCol < promptEnd) { e.Handled = true; return; }

        string keyToSend = string.Empty;
        bool contentChanged = false; // track if we need to refresh suggestions

        if (e.Key == Key.Enter)
        {
            string inputLine = GetCurrentInputLine();
            _lastSubmittedCommand = inputLine; // remember to filter remote echo
            CommandEntered?.Invoke(this, inputLine);
            // Do not feed CR locally; remote side output will follow.
            _currentInputLineAbsRow = -1;
            _currentInputEndCol = 0;
            e.Handled = true;
            ScheduleAutoCompleteRefresh(); // refresh for next prompt context
            return; // keep prompt+command until remote output arrives
        }
        else if (e.Key == Key.Back)
        {
            int editableEndForDelete = _currentInputEndCol;
            if (cursorCol > promptEnd)
            {
                var line = buffer.Lines[buffer.Y + buffer.YBase];
                int deletePos = cursorCol - 1;
                for (int i = deletePos; i < editableEndForDelete - 1 && i + 1 < line.Length; i++)
                    line[i] = line[i + 1];
                // Blank out last cell of the edited region
                int lastPos = Math.Min(editableEndForDelete - 1, line.Length - 1);
                if (lastPos >= promptEnd)
                {
                    var blank = line[lastPos]; blank.Code = 0; line[lastPos] = blank;
                }
                _terminal.SetCursor(deletePos, buffer.Y);
                // Adjust tracked end if deleting at end
                if (deletePos + 1 == _currentInputEndCol)
                    _currentInputEndCol = Math.Max(promptEnd, _currentInputEndCol - 1);
                int lastRow = buffer.Y + buffer.YBase;
                if (!_fullRedrawPending) _dirtyLines.Add(lastRow);
                RedrawTerminal(onlyRow: lastRow);
                contentChanged = true;
            }
            e.Handled = true; ScheduleAutoCompleteRefresh(); return;
        }
        else
        {
            switch (e.Key)
            {
                case Key.Delete: keyToSend = "\u001B[3~"; break;
                case Key.Escape: keyToSend = "\u001B"; break;
                case Key.Right: keyToSend = "\u001B[C"; break;
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
                        e.Handled = true;
                        ScheduleAutoCompleteRefresh();
                        return;
                    }
                    else if ((Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == ModifierKeys.Alt) && e.Key >= Key.A && e.Key <= Key.Z)
                    {
                        int ctrlCode = (int)e.Key - (int)Key.A + 1;
                        keyToSend = ((char)ctrlCode).ToString();
                        CommandEntered?.Invoke(this, keyToSend);
                        e.Handled = true;
                        ScheduleAutoCompleteRefresh();
                        return;
                    }
                    else
                    {
                        var c = KeyToChar(e.Key, Keyboard.Modifiers);
                        if (c != null)
                        {
                            if (cursorCol < editableEnd)
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
                                    _currentInputEndCol += 1;
                                    int lastRow2 = buffer.Y + buffer.YBase;
                                    if (!_fullRedrawPending) _dirtyLines.Add(lastRow2);
                                    RedrawTerminal(onlyRow: lastRow2);
                                    contentChanged = true;
                                    e.Handled = true;
                                    ScheduleAutoCompleteRefresh();
                                    return;
                                }
                            }
                            keyToSend = c.ToString();
                            _currentInputEndCol = Math.Max(_currentInputEndCol + 1, buffer.X + 1);
                            contentChanged = true;
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
        if (contentChanged) ScheduleAutoCompleteRefresh();
        UpdateAutoCompletePopupPosition();
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
        UpdateAutoCompletePopupPosition();
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
            return promptIdx + 1 < text.Length ? text[(promptIdx + 1)..] : string.Empty;
        return text;
    }

    private void TerminalCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        TerminalCanvas.Focus();
        _isFocused = true;
        bool plainLeft = e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.None;
        if (plainLeft)
        {
            if (_isCopyHighlight) CleanupHighlightAnimations();
            foreach (var rect in _selectionHighlightRects) TerminalCanvas.Children.Remove(rect);
            _selectionHighlightRects.Clear();
            _selectionStart = null; _selectionEnd = null;
            if (_terminal != null)
            {
                var pos = e.GetPosition(TerminalCanvas);
                // Use the precise DIP line height for hit-testing (avoid truncation)
                double lineHeightForCalc = _lineHeightDip > 0 ? _lineHeightDip : Math.Ceiling(_charHeight);
                int visualCol = (int)(pos.X / _charWidth);
                int visualRow = (int)(pos.Y / lineHeightForCalc);
                var buffer = _terminal.Buffer;
                int logicalRow = _renderStartRow + visualRow;
                logicalRow = Math.Clamp(logicalRow, 0, buffer.Lines.Length - 1);
                int maxCol = _terminal.Cols - 1;
                int logicalCol = Math.Clamp(visualCol, 0, maxCol);
                _selectionStart = (logicalRow, logicalCol);
                _selectionEnd = (logicalRow, logicalCol);
                _isSelecting = true;
                UpdateSelectionHighlightRect();
                TerminalCanvas.CaptureMouse();
            }
            e.Handled = true; return;
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
            e.Handled = true; return;
        }
        if (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            if (Clipboard.ContainsText()) PasteAtInputArea(Clipboard.GetText());
            e.Handled = true; return;
        }
    }

    private void TerminalCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(TerminalCanvas);
            // Use precise DIP line height for hit-testing (avoid truncation)
            double lineHeightForCalc = _lineHeightDip > 0 ? _lineHeightDip : Math.Ceiling(_charHeight);
            int col = (int)(pos.X / _charWidth);
            int row = (int)(pos.Y / lineHeightForCalc) + _renderStartRow;
            if (_isSelecting)
            {
                _selectionEnd = (row, col);
                UpdateSelectionHighlightRect();
            }
            else
            {
                _isSelecting = true; _selectionStart = (row, col); _selectionEnd = (row, col);
            }
        }
    }

    private void TerminalCanvasMouseUp(object sender, MouseButtonEventArgs e)
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
        _copyHighlightTimer?.Stop(); _copyHighlightTimer = null;
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue || _terminal == null) return;
        var (row1, col1) = _selectionStart.Value;
        var (row2, col2) = _selectionEnd.Value;
        int startRow = Math.Min(row1, row2);
        int endRow = Math.Max(row1, row2);
        int startCol = Math.Min(col1, col2);
        int endCol = Math.Max(col1, col2);
        int maxCol = _terminal.Cols - 1;
        // Use precise DIP line height for rendering selection rects
        double lineHeight = _lineHeightDip > 0 ? _lineHeightDip : Math.Ceiling(_charHeight);
        for (int row = startRow; row <= endRow; row++)
        {
            if (row < _renderStartRow) continue;
            int visualRow = row - _renderStartRow;
            int colStart = (row == startRow) ? startCol : 0;
            int colEnd = (row == endRow) ? endCol : maxCol;
            colStart = Math.Clamp(colStart, 0, maxCol);
            colEnd = Math.Clamp(colEnd, 0, maxCol);
            if (colEnd < colStart) continue;
            var rect = new Rectangle
            {
                Width = (colEnd - colStart + 1) * _charWidth,
                Height = lineHeight,
                Fill = new SolidColorBrush(Theme.SelectionColor),
                Opacity = 0.5,
                IsHitTestVisible = false
            };
            TerminalCanvas.Children.Add(rect);
            Canvas.SetLeft(rect, colStart * _charWidth);
            Canvas.SetTop(rect, visualRow * lineHeight);
            _selectionHighlightRects.Add(rect);
        }
    }

    private string GetSelectedText()
    {
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue || _terminal == null) return string.Empty;
        var (row1, col1) = _selectionStart.Value; var (row2, col2) = _selectionEnd.Value;
        int startRow = Math.Min(row1, row2); int endRow = Math.Max(row1, row2);
        int startCol = Math.Min(col1, col2); int endCol = Math.Max(col1, col2);
        var buffer = _terminal.Buffer; var sb = new StringBuilder();
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
        if (_terminal == null || IsReadOnly || string.IsNullOrEmpty(text)) return;
        var buffer = _terminal.Buffer;
        int row = buffer.Y + buffer.YBase;
        int promptEnd = GetPromptEndCol();

        // Ensure tracking for this line is initialized
        if (_currentInputLineAbsRow != row)
        {
            _currentInputLineAbsRow = row;
            _currentPromptEndCol = promptEnd;
            _currentInputEndCol = Math.Max(promptEnd, GetEditableEndCol(promptEnd));
        }
        else
        {
            // Update current end in case external changes happened
            _currentInputEndCol = Math.Max(_currentInputEndCol, GetEditableEndCol(promptEnd));
        }

        int insertCol = buffer.X;
        var line = buffer.Lines[row];

        // If inserting in the middle of existing editable content, shift remainder right
        if (insertCol < _currentInputEndCol)
        {
            int remainderLength = _currentInputEndCol - insertCol;
            int needed = text.Length;
            // Shift characters to the right (truncate if exceeds line length)
            for (int i = _currentInputEndCol - 1; i >= insertCol && i < line.Length; i++)
            {
                int target = i + needed;
                if (target >= line.Length) continue; // overflow truncated
                line[target] = line[i];
            }
        }

        // Write pasted characters
        foreach (char c in text)
        {
            if (insertCol < line.Length)
            {
                var cell = line[insertCol];
                cell.Code = c;
                line[insertCol] = cell;
            }
            insertCol++;
        }

        buffer.X = insertCol;
        // Update editable end tracking
        _currentInputEndCol = Math.Max(_currentInputEndCol, buffer.X);

        if (!_fullRedrawPending)
        {
            _dirtyLines.Add(row);
        }
        RedrawTerminal(onlyRow: row);
    }

    public void ApplyThemeProperties()
    {
        _typeface = new Typeface(Theme.FontFamily.Source);
        _selectionBrush = new SolidColorBrush(Theme.SelectionColor);
        BuildAnsiBrushes();
        if (TerminalGrid != null) TerminalGrid.Background = Theme.Background;
        UpdateCharSize();
        _fullRedrawPending = true; _dirtyLines.Clear();
        RedrawTerminal();
        _selectionStart = null; _selectionEnd = null;
    }

    private Size _lastLayoutSize = Size.Empty; private int _lastCols = -1; private int _lastRows = -1;
    private bool _startRender = false;

    private void UpdateTerminalLayoutAndSize(Size? newSize = null)
    {
        UpdateCharSize();
        Size size = newSize ?? new Size(ActualWidth, ActualHeight);
        int cols = Math.Max(10, (int)(size.Width / _charWidth));
        int rows = Math.Max(2, (int)(size.Height / _charHeight));
        // Do not set TerminalCanvas size here; canvas represents the full rendered content and is set in PerformRedraw.
        // Use cols/rows to resize the terminal emulator (based on viewport size).
        if (_terminal != null && (size != _lastLayoutSize || cols != _lastCols || rows != _lastRows))
        {
            _terminal.Resize(cols, rows); _lastLayoutSize = size; _lastCols = cols; _lastRows = rows; _fullRedrawPending = true; _dirtyLines.Clear();
        }
        TerminalSizeChanged?.Invoke(this, size);
        RedrawTerminal();
        UpdateSelectionHighlightRect();
    }

    private void EnsureAutoCompleteRefreshTimer()
    {
        if (_autoCompleteRefreshTimer != null) return;
        _autoCompleteRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _autoCompleteRefreshTimer.Tick += (s, e) =>
        {
            _autoCompleteRefreshTimer.Stop();
            if (_autoCompleteRefreshPending)
            {

                _autoCompleteRefreshPending = false;
                RaiseAutoCompleteRequest();
            }
        };
    }

    private void ScheduleAutoCompleteRefresh()
    {
        if (!_isAutoCompleteVisible) return;
        EnsureAutoCompleteRefreshTimer();
        _autoCompleteRefreshPending = true;
        _autoCompleteRefreshTimer!.Stop();
        _autoCompleteRefreshTimer.Start();
        UpdateAutoCompletePopupPosition();
    }

    private void RaiseAutoCompleteRequest()
    {
        if (_terminal == null) return;
        var args = new TabCompletionEventArgs
        {
            CurrentLine = GetCurrentInputLine(),
            CursorPosition = GetCursorPosition(),
            WorkingDirectory = "~"
        };
        TabCompletionRequested?.Invoke(this, args);
        UpdateAutoCompletePopupPosition();
    }

    private void UpdateAutoCompletePopupPosition()
    {
        if (_autoCompletePopup == null || _terminal == null)
            return;
        if (_lineHeightDip <= 0) _lineHeightDip = Math.Ceiling(_charHeight);
        var buffer = _terminal.Buffer;
        int absRow = buffer.Y + buffer.YBase;
        int visualRow = absRow - _renderStartRow;
        if (visualRow < 0) visualRow = 0;
        double x = buffer.X * _charWidth;
        double y = (visualRow + 1) * _lineHeightDip; // below cursor cell
        if (_autoCompletePopup.Child is FrameworkElement fe && (fe.ActualWidth == 0 || fe.ActualHeight == 0))
            fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double popupWidth = (_autoCompletePopup.Child as FrameworkElement)?.ActualWidth ?? 300;
        double popupHeight = (_autoCompletePopup.Child as FrameworkElement)?.ActualHeight ?? 150;
        double maxX = Math.Max(0, TerminalCanvas.Width - popupWidth - 4);
        if (x > maxX) x = maxX; if (x < 0) x = 0;
        double maxY = Math.Max(0, TerminalCanvas.Height - popupHeight - 4);
        if (y > maxY)
        {
            double aboveY = visualRow * _lineHeightDip - popupHeight - 2;
            y = aboveY < 0 ? 0 : aboveY;
        }
        bool changed = !DoubleUtil.AreClose(_autoCompletePopup.HorizontalOffset, x) || !DoubleUtil.AreClose(_autoCompletePopup.VerticalOffset, y);
        _autoCompletePopup.HorizontalOffset = x;
        _autoCompletePopup.VerticalOffset = y;
        if (!changed)
        {
            _autoCompletePopup.VerticalOffset += 0.01;
            _autoCompletePopup.VerticalOffset -= 0.01;
        }
    }

    private static class DoubleUtil
    {
        public static bool AreClose(double a, double b) => Math.Abs(a - b) < 0.5;
    }

    private static bool IsBold(int attr) => ((attr >> 18) & 1) != 0;
    private static bool IsInverse(int attr) => ((attr >> 18) & 0x40) != 0;

    private Brush GetAnsiForeground(int attr, bool inverse)
    {
        int fg = (attr >> 9) & 0x1ff;
        int bg = attr & 0x1ff;
        if (inverse) (fg, bg) = (bg, fg);
        if (fg >= 0 && fg < Theme.AnsiForegroundPalette.Count)
            return Theme.AnsiForegroundPalette[fg].ToBrush();
        return (Theme.Foreground as SolidColorBrush) ?? Brushes.LightGray;
    }

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

    private int GetEditableEndCol(int promptEnd)
    {
        if (_terminal == null) return promptEnd;
        if (_currentInputLineAbsRow == (_terminal.Buffer.Y + _terminal.Buffer.YBase) && _currentInputEndCol > 0) return _currentInputEndCol;
        var buffer = _terminal.Buffer; var line = buffer.Lines[buffer.Y + buffer.YBase]; int last = -1;
        for (int i = promptEnd; i < line.Length; i++) { var cell = line[i]; char c = cell.Code == 0 ? ' ' : (char)cell.Code; if (c != ' ') last = i; }
        return Math.Max(promptEnd, last + 1);
    }

    private void StartCopyHighlightTransition()
    {
        _copyHighlightTimer?.Stop();
        _copyHighlightTimer = null;
        _isCopyHighlight = true;
        if (_selectionHighlightRects.Count == 0) { _isCopyHighlight = false; return; }
        var defaultSelectionColor = Theme.SelectionColor;
        var copySelectionColor = Theme.CopySelectionColor;
        var animationsToDefault = new List<ColorAnimation>();
        foreach (var rect in _selectionHighlightRects)
        {
            if (rect.Fill is SolidColorBrush brush)
            {
                var animToCopy = new ColorAnimation { From = defaultSelectionColor, To = copySelectionColor, Duration = TimeSpan.FromMilliseconds(400) };
                var animBack = new ColorAnimation { From = copySelectionColor, To = defaultSelectionColor, Duration = TimeSpan.FromMilliseconds(600) };
                animationsToDefault.Add(animBack);
                brush.BeginAnimation(SolidColorBrush.ColorProperty, animToCopy);
            }
        }
        _copyHighlightTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _copyHighlightTimer.Tick += (s, e) =>
        {
            _copyHighlightTimer.Stop(); _copyHighlightTimer = null;
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
                                if (r.Fill is SolidColorBrush b)
                                { b.Color = Theme.SelectionColor; b.BeginAnimation(SolidColorBrush.ColorProperty, null); }
                        };
                    }
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, back);
                }
            }
        };
        _copyHighlightTimer.Start();
    }

    // XAML references MouseDown="TerminalControl_MouseDown" – provide wrapper
    private void TerminalControl_MouseDown(object sender, MouseButtonEventArgs e) => TerminalCanvasMouseDown(sender, e);

    private void RedrawTerminal(int? onlyRow = null)
    {
        if (_terminal == null || !_startRender) return;
        if (onlyRow.HasValue && !_fullRedrawPending) _dirtyLines.Add(onlyRow.Value);
        else if (!onlyRow.HasValue && _dirtyLines.Count == 0 && !_fullRedrawPending) _fullRedrawPending = true;
        lock (_redrawLock)
        {
            if (_isRedrawing) { _redrawRequested = true; return; }
            _isRedrawing = true; _redrawRequested = false;
        }
        PerformRedraw();
    }

    private void PerformRedraw()
    {
        try
        {
            if (_terminal == null) { CompleteRedraw(); return; }
            var buffer = _terminal.Buffer; int cols = _terminal.Cols; int totalRows = buffer.Lines.Length;
            if (totalRows <= 0) { CompleteRedraw(); return; }
            int previousRenderStart = _renderStartRow;
            _renderStartRow = totalRows > MaxRenderedLines ? totalRows - MaxRenderedLines : 0;
            int renderRowCount = totalRows - _renderStartRow;
            double cw = _charWidth; double ch = _charHeight;
            // Align character cell to device pixels to avoid fuzzy subpixel rendering
            var dpi = VisualTreeHelper.GetDpi(this);
            double dpiScaleX = dpi.DpiScaleX;
            double dpiScaleY = dpi.DpiScaleY;
            // Compute integer pixel sizes for character cell
            double pixelCW = Math.Max(1.0, Math.Round(cw * dpiScaleX));
            double pixelCH = Math.Max(1.0, Math.Round(ch * dpiScaleY));
            // Convert back to device-independent units that align to whole device pixels
            cw = pixelCW / dpiScaleX;
            ch = pixelCH / dpiScaleY;
            // Persist aligned char sizes so layout and interaction use the same metrics as the bitmap rendering
            _charWidth = cw;
            _charHeight = ch;
            // Store DIP line height for layout; keep pixelLineHeight for bitmap render sizes
            double lineHeightDip = ch; if (lineHeightDip <= 0) lineHeightDip = 1.0; _lineHeightDip = lineHeightDip;
            int linePixelHeight = (int)pixelCH; if (linePixelHeight <= 0) linePixelHeight = 1;
            _linePixelHeight = linePixelHeight;
            int pixelWidth = (int)Math.Ceiling(cols * pixelCW); if (pixelWidth <= 0) pixelWidth = 1;
            int pixelHeight = linePixelHeight * renderRowCount;
            // Device DPI in DPI units (pixels per logical 96 DPI unit)
            double dpiX = 96.0 * dpiScaleX;
            double dpiY = 96.0 * dpiScaleY;
            bool allocate = _surface == null || _surface.PixelWidth != pixelWidth || _surface.PixelHeight != pixelHeight || Math.Abs(_surface.DpiX - dpiX) > 0.1 || Math.Abs(_surface.DpiY - dpiY) > 0.1;
            if (allocate)
            {
                // Create writeable bitmap with actual device DPI so the bitmap maps 1:1 to device pixels
                _surface = new WriteableBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32, null);
                _fullRedrawPending = true; _dirtyLines.Clear();
            }
            if (_lastRenderedBufferLines == null || _lastRenderedBufferLines.Length != totalRows)
            {
                _lastRenderedBufferLines = new string[totalRows];
            }
            IEnumerable<int> linesToDraw = _fullRedrawPending ? Enumerable.Range(_renderStartRow, renderRowCount) : _dirtyLines.Where(r => r >= _renderStartRow).OrderBy(r => r).ToArray();
            if (!linesToDraw.Any()) { CompleteRedraw(); return; }
            var theme = Theme;
            var fgDefault = theme.Foreground as SolidColorBrush ?? Brushes.LightGray;
            var bgDefault = theme.Background as SolidColorBrush ?? Brushes.Black;
            var cursorBrush = (theme.CursorColor as SolidColorBrush) ?? fgDefault;
            foreach (var row in linesToDraw)
            {
                if (row < 0 || row >= buffer.Lines.Length) continue;
                int visualRow = row - _renderStartRow;
                if (visualRow < 0 || visualRow >= renderRowCount) continue;
                int destY = visualRow * linePixelHeight;
                if (destY + linePixelHeight > _surface?.PixelHeight) continue;
                var line = buffer.Lines[row];
                var dv = new DrawingVisual();
                // Prefer Display formatting and ClearType when rendering text to a bitmap
                RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);
                RenderOptions.SetClearTypeHint(dv, ClearTypeHint.Enabled);
                TextOptions.SetTextFormattingMode(dv, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(dv, TextRenderingMode.ClearType);
                using (var dc = dv.RenderOpen())
                {
                    // Convert pixel dimensions to DIPs for drawing into the visual
                    double dipPixelWidth = pixelWidth / dpiScaleX;
                    double dipLineHeight = linePixelHeight / dpiScaleY;
                    dc.DrawRectangle(bgDefault, null, new Rect(0, 0, dipPixelWidth, dipLineHeight));
                    int lineLen = line.Length;
                    for (int col = 0; col < cols; col++)
                    {
                        char chChar = ' ';
                        int attr = CharData.DefaultAttr;
                        if (col < lineLen)
                        {
                            var cell = line[col]; chChar = cell.Code != 0 ? (char)cell.Code : ' '; attr = cell.Attribute;
                        }
                        bool isCursor = (row == buffer.Y + buffer.YBase && col == buffer.X);
                        bool inverse = IsInverse(attr) ^ (isCursor && _isFocused);
                        Brush fg = GetAnsiForeground(attr, inverse);
                        Brush bg = bgDefault;
                        dc.DrawRectangle(bg, null, new Rect(col * cw, 0, cw, linePixelHeight));
#pragma warning disable CS0618
                        var ft = new FormattedText(chChar.ToString(), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                            new Typeface(theme.FontFamily, FontStyles.Normal, IsBold(attr) ? FontWeights.Bold : FontWeights.Normal, FontStretches.Medium),
                            theme.FontSize, fg, VisualTreeHelper.GetDpi(this).PixelsPerDip);
#pragma warning restore CS0618
                        // Draw text snapped to device pixels
                        double x = Math.Round(col * cw * dpiScaleX) / dpiScaleX;
                        dc.DrawText(ft, new Point(x, 0));
                        if (isCursor && _isFocused)
                        {
                            dc.DrawRectangle(cursorBrush, null, new Rect(col * cw, 0, cw, lineHeightDip));
#pragma warning disable CS0618
                            var ftC = new FormattedText(chChar.ToString(), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                new Typeface(theme.FontFamily, FontStyles.Normal, IsBold(attr) ? FontWeights.Bold : FontWeights.Normal, FontStretches.Medium),
                                theme.FontSize, bgDefault, VisualTreeHelper.GetDpi(this).PixelsPerDip);
#pragma warning restore CS0618
                            double cx = Math.Round(col * cw * dpiScaleX) / dpiScaleX;
                            dc.DrawText(ftC, new Point(cx, 0));
                        }
                    }
                }
                // Use actual device DPI when creating bitmap to avoid scaling artifacts
                var lineBmp = new RenderTargetBitmap(pixelWidth, linePixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
                lineBmp.Render(dv);
                int stride = pixelWidth * 4; var pixels = new byte[stride * linePixelHeight];
                lineBmp.CopyPixels(pixels, stride, 0);
                if (destY + linePixelHeight <= _surface.PixelHeight)
                    _surface!.WritePixels(new Int32Rect(0, destY, pixelWidth, linePixelHeight), pixels, stride, 0);
                if (row < _lastRenderedBufferLines.Length)
                    _lastRenderedBufferLines[row] = BufferLineToString(line, cols);
            }
            _dirtyLines.Clear(); _fullRedrawPending = false;
            TerminalBitmapImage.Source = _surface;
            // Determine the bitmap's natural size in DIPs using its DPI so the ScrollViewer sees correct content size
            // Compute DIP width/height from pixel size and DPI scale
            double dipWidth = pixelWidth / dpiScaleX;
            double dipHeight = pixelHeight / dpiScaleY;
            double srcDipWidth = dipWidth;
            double srcDipHeight = dipHeight;
            // Set Image and Canvas to the bitmap's DIP size so the ScrollViewer can scroll the full image.
            // Also set TerminalGrid MinWidth/MinHeight to ensure ScrollViewer extent accounts for content size.
            TerminalBitmapImage.Width = srcDipWidth; TerminalBitmapImage.Height = srcDipHeight;
            TerminalCanvas.Width = srcDipWidth; TerminalCanvas.Height = srcDipHeight - 20;
            if (TerminalGrid != null)
            {
                TerminalGrid.MinWidth = Math.Max(TerminalGrid.MinWidth, srcDipWidth);
                TerminalGrid.MinHeight = Math.Max(TerminalGrid.MinHeight, srcDipHeight);
            }
            if (Cursor != null) Cursor.Visibility = Visibility.Collapsed;
            // Diagnostic logging to help track DPI / size mismatches
            try
            {
                var dpiInfo = VisualTreeHelper.GetDpi(this);
                Debug.WriteLine($"[TerminalRender] dpiScaleX={dpiInfo.DpiScaleX} dpiScaleY={dpiInfo.DpiScaleY} dpiX={dpiInfo.PixelsPerInchX} dpiY={dpiInfo.PixelsPerInchY}");
                Debug.WriteLine($"[TerminalRender] pixelWidth={pixelWidth} pixelHeight={pixelHeight} dipWidth={dipWidth:F2} dipHeight={dipHeight:F2} srcDipWidth={srcDipWidth:F2} srcDipHeight={srcDipHeight:F2}");
                Debug.WriteLine($"[TerminalRender] TerminalBitmapImage.ActualWidth={TerminalBitmapImage.ActualWidth} ActualHeight={TerminalBitmapImage.ActualHeight}");
                Debug.WriteLine($"[TerminalRender] TerminalCanvas.ActualWidth={TerminalCanvas.ActualWidth} ActualHeight={TerminalCanvas.ActualHeight}");
                if (TerminalGrid != null) Debug.WriteLine($"[TerminalRender] TerminalGrid.ActualWidth={TerminalGrid.ActualWidth} ActualHeight={TerminalGrid.ActualHeight}");
                if (TerminalScrollViewer != null) Debug.WriteLine($"[TerminalRender] ScrollViewer.ViewportWidth={TerminalScrollViewer.ViewportWidth} ViewportHeight={TerminalScrollViewer.ViewportHeight}");
            }
            catch { }
        }
        catch (IndexOutOfRangeException ex)
        {
            Debug.WriteLine($"PerformRedraw index issue suppressed: {ex.Message}");
            _fullRedrawPending = true; _dirtyLines.Clear();
        }
        catch (Exception ex) { Debug.WriteLine($"Incremental redraw exception: {ex}"); }
        finally { CompleteRedraw(); }
    }

    private void CompleteRedraw()
    {
        bool again;
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
            char c = (char)('a' + (key - Key.A)); if ((modifiers & ModifierKeys.Shift) != 0) c = char.ToUpper(c); return c;
        }
        if (key >= Key.D0 && key <= Key.D9)
        {
            char c = (char)('0' + (key - Key.D0));
            if ((modifiers & ModifierKeys.Shift) != 0)
            {
                string shifted = ")!@#$%^&*("; c = shifted[key - Key.D0];
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

    public bool HasSelection() => _selectionStart.HasValue && _selectionEnd.HasValue;
    public void CopySelection()
    {
        if (HasSelection())
        {
            var text = GetSelectedText();
            if (!string.IsNullOrEmpty(text)) { Clipboard.SetText(text); StartCopyHighlightTransition(); }
        }
    }
    public void PasteText(string text) => PasteAtInputArea(text);
    public void PasteFromClipboard()
    {
        if (Clipboard.ContainsText()) PasteAtInputArea(Clipboard.GetText());
    }
    public void SelectAll()
    {
        if (_terminal == null) return; var buffer = _terminal.Buffer; if (buffer.Lines.Length == 0) return;
        _selectionStart = (0, 0); _selectionEnd = (buffer.Lines.Length - 1, _terminal.Cols - 1); UpdateSelectionHighlightRect();
    }
    public new void Focus() => TerminalCanvas.Focus();

    public void ShowAutoCompleteResults(AutoCompleteResult result)
    {
        if (result == null || !result.HasSuggestions) { HideAutoComplete(); return; }
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
            UpdateAutoCompletePopupPosition();
            _autoCompletePopup.IsOpen = true; _isAutoCompleteVisible = true;
            if (_autoCompleteListBox.Items.Count > 0)
            {
                _autoCompleteListBox.SelectedIndex = 0;
                _autoCompleteListBox.ScrollIntoView(_autoCompleteListBox.SelectedItem);
            }
        }
    }

    public void HideAutoComplete()
    {
        if (_autoCompletePopup != null) _autoCompletePopup.IsOpen = false; _isAutoCompleteVisible = false;
    }

    public int GetCursorPosition()
    {
        if (_terminal == null) return 0; var buffer = _terminal.Buffer; int promptEnd = GetPromptEndCol(); return Math.Max(0, buffer.X - promptEnd);
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
        spFactory.AppendChild(textFactory); spFactory.AppendChild(descFactory);
        dataTemplate.VisualTree = spFactory; _autoCompleteListBox.ItemTemplate = dataTemplate;
        _autoCompleteListBox.PreviewKeyDown += AutoCompleteListBox_PreviewKeyDown;
        _autoCompleteListBox.MouseDoubleClick += (s, e) => AcceptAutoCompleteSelection();
        _autoCompletePopup = new Popup
        {
            Child = _autoCompleteListBox,
            PlacementTarget = TerminalCanvas,
            Placement = PlacementMode.Relative,
            StaysOpen = false,
            Focusable = false,
            Opacity = .8
        };
        _autoCompletePopup.Closed += (s, e) => _isAutoCompleteVisible = false;
    }

    // Autocomplete list key handling
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
            // Replace last token with suggestion
            var parts = current.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count == 0)
            {
                AddInput(item.Text);
            }
            else
            {
                parts[^1] = item.Text;
                string newLine = string.Join(" ", parts);
                // Clear current input region before inserting replacement
                // Simplistic: feed backspaces to prompt end then reinsert
                if (_terminal != null)
                {
                    int promptEnd = GetPromptEndCol();
                    int existingLen = _currentInputEndCol - promptEnd;
                    if (existingLen > 0)
                    {
                        _terminal.Feed(new string('\b', existingLen));
                    }
                }
                AddInput(newLine);
            }
        }
        HideAutoComplete();
        Focus();
    }

    // ITerminalDelegate implementations (unused optional)
    public void ShowCursor(Terminal source) { }
    public void SetTerminalTitle(Terminal source, string title) { }
    public void SetTerminalIconTitle(Terminal source, string title) { }
    void ITerminalDelegate.SizeChanged(Terminal source) { }
    public void Send(byte[] data) { }
    public string WindowCommand(Terminal source, WindowManipulationCommand command, params int[] args) => string.Empty;
    public bool IsProcessTrusted() => true;

    private class AutoCompleteItem
    {
        public string Text { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CompletionType Type { get; set; }
    }
}