using System.ComponentModel;

namespace ScratchShell.UserControls.TerminalControl
{
    public class TerminalState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _cursorRow = 0;
        private int _cursorCol = 0;
        private int _cursorColumn = 0;
        private bool _cursorVisible = true;
        private string? _windowTitle;
        private string? _iconName;
        private string? _foregroundColor;
        private string? _backgroundColor;
        private string? _cursorColor;
        private string? _clipboard;

        private bool _lineWrap = true;
        private bool _alternateScreenBuffer = false;
        private bool _applicationCursorKeys = false;
        private bool _columns132Mode = false;
        private bool _reverseVideo = false;
        private bool _originMode = false;
        private bool _blinkingCursor = false;

        private bool _mouseTrackingEnabled = false;
        private bool _mouseHighlightTracking = false;
        private bool _mouseButtonTracking = false;
        private bool _mouseMotionTracking = false;
        private bool _focusEventTracking = false;

        public int CursorRow
        {
            get => _cursorRow;
            set
            {
                if (_cursorRow != value)
                {
                    _cursorRow = value;
                    OnPropertyChanged(nameof(CursorRow));
                }
            }
        }

        public int CursorCol
        {
            get => _cursorCol;
            set
            {
                if (_cursorCol != value)
                {
                    _cursorCol = value;
                    OnPropertyChanged(nameof(CursorCol));
                }
            }
        }

        public int CursorColumn
        {
            get => _cursorColumn;
            set
            {
                if (_cursorColumn != value)
                {
                    _cursorColumn = value;
                    OnPropertyChanged(nameof(CursorColumn));
                }
            }
        }

        public bool CursorVisible
        {
            get => _cursorVisible;
            set
            {
                if (_cursorVisible != value)
                {
                    _cursorVisible = value;
                    OnPropertyChanged(nameof(CursorVisible));
                }
            }
        }

        public List<string> ScreenBuffer { get; set; } = new List<string>();

        public string? WindowTitle
        {
            get => _windowTitle;
            internal set
            {
                if (_windowTitle != value)
                {
                    _windowTitle = value;
                    OnPropertyChanged(nameof(WindowTitle));
                }
            }
        }

        public string? IconName
        {
            get => _iconName;
            internal set
            {
                if (_iconName != value)
                {
                    _iconName = value;
                    OnPropertyChanged(nameof(IconName));
                }
            }
        }

        public string? ForegroundColor
        {
            get => _foregroundColor;
            set
            {
                if (_foregroundColor != value)
                {
                    _foregroundColor = value;
                    OnPropertyChanged(nameof(ForegroundColor));
                }
            }
        }

        public string? BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                if (_backgroundColor != value)
                {
                    _backgroundColor = value;
                    OnPropertyChanged(nameof(BackgroundColor));
                }
            }
        }

        public string? CursorColor
        {
            get => _cursorColor;
            set
            {
                if (_cursorColor != value)
                {
                    _cursorColor = value;
                    OnPropertyChanged(nameof(CursorColor));
                }
            }
        }

        public Dictionary<int, string> ColorPalette { get; set; } = new();

        public Dictionary<string, string> UnhandledOsc { get; set; } = new();

        public string? Clipboard
        {
            get => _clipboard;
            set
            {
                if (_clipboard != value)
                {
                    _clipboard = value;
                    OnPropertyChanged(nameof(Clipboard));
                }
            }
        }

        public bool LineWrap
        {
            get => _lineWrap;
            set
            {
                if (_lineWrap != value)
                {
                    _lineWrap = value;
                    OnPropertyChanged(nameof(LineWrap));
                }
            }
        }

        public bool AlternateScreenBuffer
        {
            get => _alternateScreenBuffer;
            set
            {
                if (_alternateScreenBuffer != value)
                {
                    _alternateScreenBuffer = value;
                    OnPropertyChanged(nameof(AlternateScreenBuffer));
                }
            }
        }

        public bool ApplicationCursorKeys
        {
            get => _applicationCursorKeys;
            set
            {
                if (_applicationCursorKeys != value)
                {
                    _applicationCursorKeys = value;
                    OnPropertyChanged(nameof(ApplicationCursorKeys));
                }
            }
        }

        public bool Columns132Mode
        {
            get => _columns132Mode;
            set
            {
                if (_columns132Mode != value)
                {
                    _columns132Mode = value;
                    OnPropertyChanged(nameof(Columns132Mode));
                }
            }
        }

        public bool ReverseVideo
        {
            get => _reverseVideo;
            set
            {
                if (_reverseVideo != value)
                {
                    _reverseVideo = value;
                    OnPropertyChanged(nameof(ReverseVideo));
                }
            }
        }

        public bool OriginMode
        {
            get => _originMode;
            set
            {
                if (_originMode != value)
                {
                    _originMode = value;
                    OnPropertyChanged(nameof(OriginMode));
                }
            }
        }

        public bool BlinkingCursor
        {
            get => _blinkingCursor;
            set
            {
                if (_blinkingCursor != value)
                {
                    _blinkingCursor = value;
                    OnPropertyChanged(nameof(BlinkingCursor));
                }
            }
        }

        // Mouse modes
        public bool MouseTrackingEnabled
        {
            get => _mouseTrackingEnabled;
            set
            {
                if (_mouseTrackingEnabled != value)
                {
                    _mouseTrackingEnabled = value;
                    OnPropertyChanged(nameof(MouseTrackingEnabled));
                }
            }
        }

        public bool MouseHighlightTracking
        {
            get => _mouseHighlightTracking;
            set
            {
                if (_mouseHighlightTracking != value)
                {
                    _mouseHighlightTracking = value;
                    OnPropertyChanged(nameof(MouseHighlightTracking));
                }
            }
        }

        public bool MouseButtonTracking
        {
            get => _mouseButtonTracking;
            set
            {
                if (_mouseButtonTracking != value)
                {
                    _mouseButtonTracking = value;
                    OnPropertyChanged(nameof(MouseButtonTracking));
                }
            }
        }

        public bool MouseMotionTracking
        {
            get => _mouseMotionTracking;
            set
            {
                if (_mouseMotionTracking != value)
                {
                    _mouseMotionTracking = value;
                    OnPropertyChanged(nameof(MouseMotionTracking));
                }
            }
        }

        public bool FocusEventTracking
        {
            get => _focusEventTracking;
            set
            {
                if (_focusEventTracking != value)
                {
                    _focusEventTracking = value;
                    OnPropertyChanged(nameof(FocusEventTracking));
                }
            }
        }

        // Optional: track unhandled modes
        public Dictionary<string, bool> UnhandledModes { get; set; } = new();

        // Save/restore cursor methods
        private (int Row, int Col)? _savedCursor;

        public void SaveCursor() => _savedCursor = (CursorRow, CursorCol);

        public void RestoreCursor()
        {
            if (_savedCursor.HasValue)
            {
                CursorRow = _savedCursor.Value.Row;
                CursorCol = _savedCursor.Value.Col;
            }
        }

        public void ClearScreen()
        {
            ScreenBuffer.Clear();
            OnPropertyChanged(nameof(ScreenBuffer));
        }

        public void ClearLine()
        {
            if (CursorRow >= 0 && CursorRow < ScreenBuffer.Count)
            {
                ScreenBuffer[CursorRow] = string.Empty;
                OnPropertyChanged(nameof(ScreenBuffer));
            }
        }

        /// <summary>
        /// Resets colors or palettes based on OSC reset commands (104-110)
        /// </summary>
        public void ResetColor(string code)
        {
            switch (code)
            {
                case "104":
                    ForegroundColor = null;
                    break;

                case "105":
                    BackgroundColor = null;
                    break;

                case "106":
                    CursorColor = null;
                    break;

                case "107":
                    ColorPalette.Clear();
                    OnPropertyChanged(nameof(ColorPalette));
                    break;

                case "110":
                    ForegroundColor = null;
                    BackgroundColor = null;
                    CursorColor = null;
                    ColorPalette.Clear();
                    OnPropertyChanged(nameof(ColorPalette));
                    break;
            }
        }

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}