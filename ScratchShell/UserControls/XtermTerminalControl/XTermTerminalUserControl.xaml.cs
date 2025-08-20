using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using XtermSharp;
using Color = System.Windows.Media.Color;
using Paragraph = System.Windows.Documents.Paragraph;

namespace ScratchShell.UserControls.XtermTerminalControl
{
    public partial class XTermTerminalUserControl : UserControl, ITerminal
    {
        private readonly Terminal _terminal;
        private int _lastRenderedLine = 0;
        // Track where the current input starts
        private TextPointer? _promptStart;
        private Paragraph? _promptParagraph;
        private Run _promptRun;

        public XTermTerminalUserControl()
        {
            InitializeComponent();
            _terminal = new Terminal();

            Loaded += (s, e) =>
            {
                WritePrompt();
            };

            TerminalBox.PreviewKeyDown += TerminalBox_PreviewKeyDown;
        }

        #region ITerminal Implementation

        public event ITerminal.TerminalCommandHandler? CommandEntered;
        public event ITerminal.TerminalSizeHandler? TerminalSizeChanged;

        public string InputLineSyntax { get; set; } = "$ ";
        public bool IsReadOnly
        {
            get => TerminalBox.IsReadOnly;
            set => TerminalBox.IsReadOnly = value;
        }

        public string Text
        {
            get
            {
                var buffer = _terminal.Buffer;
                var lines = new List<string>();

                for (int i = 0; i < buffer.Lines.Length; i++)
                {
                    var line = buffer.Lines[i];
                    if (line == null) continue;

                    var sb = new StringBuilder();
                    for (int j = 0; j < line.Length; j++)
                    {
                        var cell = line[j];
                        char c = cell.Code == 0 ? ' ' : (char)cell.Code;
                        sb.Append(c);
                    }
                    lines.Add(sb.ToString().TrimEnd());
                }

                return string.Join(Environment.NewLine, lines);
            }
        }

        public void AddOutput(string v)
        {
            try
            {
                v = Environment.NewLine + v;
                _terminal.Feed(v);
                RenderBufferIncremental();
                WritePrompt(); // prompt starts directly on a new line
                ScrollToEnd();
            }
            catch (Exception)
            {

                throw;
            }
        }


        #endregion

        #region Rendering

        private void RenderBufferIncremental()
        {

            var buffer = _terminal.Buffer;

            Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < buffer.Lines.Length; i++)
                {
                    var line = buffer.Lines[i];
                    Debug.WriteLine(line.TranslateToString(false).ToString());
                }
                var bufferLinepdate = 0;
                for (int i = _lastRenderedLine; i < buffer.Lines.Length; i++)
                {
                    var line = buffer.Lines[i];
                    if (line == null) continue;

                    var lineText = line.TranslateToString(false);
                    var p = new Paragraph { Margin = new Thickness(0) };

                    var sb = new StringBuilder();
                    Color? lastFg = null;
                    Color? lastBg = null;

                    for (int j = 0; j < line.Length; j++)
                    {
                        var cell = line[j];
                        char c = cell.Code == 0 ? ' ' : (char)cell.Code;

                        int bg = cell.Attribute & 0x1FF;
                        int fg = (cell.Attribute >> 9) & 0x1FF;

                        Color fgColor = fg == Renderer.DefaultColor ? Colors.LightGray : ColorForIndex(fg);
                        Color bgColor = bg == Renderer.DefaultColor ? Colors.Transparent : ColorForIndex(bg);

                        if (lastFg == null || lastBg == null || fgColor != lastFg || bgColor != lastBg)
                        {
                            // flush existing buffer into a Run
                            if (sb.Length > 0)
                            {
                                p.Inlines.Add(new Run(sb.ToString())
                                {
                                    Foreground = new SolidColorBrush(lastFg.Value),
                                    Background = new SolidColorBrush(lastBg.Value)
                                });
                                sb.Clear();
                            }

                            lastFg = fgColor;
                            lastBg = bgColor;
                        }

                        sb.Append(c);
                    }

                    // flush remaining
                    if (sb.Length > 0)
                    {
                        p.Inlines.Add(new Run(sb.ToString())
                        {
                            Foreground = new SolidColorBrush(lastFg.Value),
                            Background = new SolidColorBrush(lastBg.Value)
                        });
                    }

                    // skip empty whitespace-only lines
                    string text = new TextRange(p.ContentStart, p.ContentEnd).Text;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        TerminalBox.Document.Blocks.Add(p);
                        bufferLinepdate++;
                    }
                }

                // update last rendered line
                _lastRenderedLine += bufferLinepdate;
            });
        }

        private static Color ColorForIndex(int index) => index switch
        {
            0 => Colors.Black,
            1 => Colors.DarkRed,
            2 => Colors.DarkGreen,
            3 => Colors.Olive,
            4 => Colors.DarkBlue,
            5 => Colors.DarkMagenta,
            6 => Colors.DarkCyan,
            7 => Colors.LightGray,
            8 => Colors.DarkGray,
            9 => Colors.Red,
            10 => Colors.Green,
            11 => Colors.Yellow,
            12 => Colors.Blue,
            13 => Colors.Magenta,
            14 => Colors.Cyan,
            15 => Colors.White,
            _ => Colors.Transparent
        };

        private void ScrollToEnd()
        {
            TerminalBox.CaretPosition = TerminalBox.Document.ContentEnd;
            TerminalBox.ScrollToEnd();
        }

        #endregion

        #region Input Handling

        private void TerminalBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Enter)
                {
                    string input = GetCurrentInput();
                    CommandEntered?.Invoke(this, input);

                    e.Handled = true;
                }
                else if (e.Key != Key.Up || e.Key != Key.Left || e.Key != Key.Right || e.Key != Key.Down || e.Key != Key.Home)
                {
                    if (_promptStart == null)
                        return;
                    // Prevent deleting or moving before prompt
                    if ((!IsCaretAfterPrompt() && !((e.Key == Key.LeftShift || e.Key == Key.Right || e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl || e.Key == Key.Up || e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Down) || e.Key == Key.Home))
                        || (e.Key == Key.Back && !IsBackAfterPrompt()))
                    {
                        TerminalBox.CaretPosition = _promptStart;
                        e.Handled = true;
                    }
                }
            }
            catch (Exception)
            {

            }

        }
        private bool IsBackAfterPrompt()
        {
            if (_promptStart == null)
                return false;

            var caretPos = TerminalBox.CaretPosition;

            if (caretPos.Paragraph != _promptStart.Paragraph)
                return false;

            int offset = _promptStart.GetOffsetToPosition(caretPos);
            return offset >= 2;
        }
        private bool IsCaretAfterPrompt()
        {
            if (_promptStart == null)
                return false;
            var terminalOffset = TerminalBox.CaretPosition.GetOffsetToPosition(TerminalBox.Document.ContentEnd);
            if (terminalOffset == 2)
            {
                return true;
            }
            var caretPos = TerminalBox.CaretPosition;

            if (caretPos.Paragraph != _promptStart.Paragraph)
                return false;

            int offset = _promptStart.GetOffsetToPosition(caretPos);

            return offset >= 0;
        }

        private string GetCurrentInput()
        {
            if (_promptStart == null || _promptParagraph == null)
                return "";

            var inputRange = new TextRange(_promptStart, _promptParagraph.ContentEnd);
            string input = inputRange.Text.TrimEnd('\r', '\n');
            Debug.WriteLine($"Raw Input: [{input}]");
            return input;
        }

        private void WritePrompt()
        {
            try
            {
                if (TerminalBox.Document.Blocks.LastBlock is not Paragraph targetParagraph)
                    return;

                // Debug: dump all blocks
                int i = 0;
                foreach (var item in TerminalBox.Document.Blocks)
                {
                    if (item is Paragraph para)
                    {
                        var r = new TextRange(para.ContentStart, para.ContentEnd);
                        Debug.WriteLine($"{i++}:{r.Text}");
                    }
                }

                // Trim trailing newlines/spaces before prompt
                var range = new TextRange(targetParagraph.ContentStart, targetParagraph.ContentEnd);
                range.Text = range.Text.TrimEnd();
                // --- static prompt text ---
                _promptRun = new Run(InputLineSyntax) { Foreground = Brushes.LightGreen };
                _promptRun.Text = " ";
                targetParagraph.Inlines.Add(_promptRun);


                // --- input area run ---
       
                _promptStart = targetParagraph.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
                _promptParagraph = targetParagraph;
                TerminalBox.CaretPosition = _promptStart;
                TerminalBox.Focus();

                ScrollToEnd();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("WritePrompt error: " + ex);
            }
        }



        #endregion
    }
}
