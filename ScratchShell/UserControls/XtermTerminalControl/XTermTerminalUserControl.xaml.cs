using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using XtermSharp;
using Color = System.Windows.Media.Color;

namespace ScratchShell.UserControls.XtermTerminalControl
{
    public partial class XTermTerminalUserControl : UserControl, ITerminal
    {
        private readonly Terminal _terminal;

        // Track where the current input starts
        private TextPointer? _promptStart;
        private Paragraph? _promptParagraph;

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
            _terminal.Feed(v);
            RenderBuffer();
            WritePrompt(); // prompt starts directly on a new line
            ScrollToEnd();
        }


        #endregion

        #region Rendering

        private void RenderBuffer()
        {
            var buffer = _terminal.Buffer;

            Dispatcher.Invoke(() =>
            {
                TerminalBox.Document.Blocks.Clear();

                for (int i = 0; i < buffer.Lines.Length; i++)
                {
                    var line = buffer.Lines[i];
                    if (line == null) continue;

                    var p = new Paragraph { Margin = new Thickness(0) };

                    for (int j = 0; j < line.Length; j++)
                    {
                        var cell = line[j];
                        char c = cell.Code == 0 ? ' ' : (char)cell.Code;

                        int bg = cell.Attribute & 0x1FF;
                        int fg = (cell.Attribute >> 9) & 0x1FF;

                        Color fgColor = fg == Renderer.DefaultColor ? Colors.LightGray : ColorForIndex(fg);
                        Color bgColor = bg == Renderer.DefaultColor ? Colors.Transparent : ColorForIndex(bg);

                        var run = new Run(c.ToString())
                        {
                            Foreground = new SolidColorBrush(fgColor),
                            Background = new SolidColorBrush(bgColor)
                        };

                        p.Inlines.Add(run);
                    }

                    TerminalBox.Document.Blocks.Add(p);
                }
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
        private bool IsBackAfterPrompt()
        {
            if (_promptStart == null)
                return false;

            var caretPos = TerminalBox.CaretPosition;

            if (caretPos.Paragraph != _promptStart.Paragraph)
                return false;

            int offset = _promptStart.GetOffsetToPosition(caretPos);
            return offset > 0;
        }
        private bool IsCaretAfterPrompt()
        {
            if (_promptStart == null)
                return false;

            var caretPos = TerminalBox.CaretPosition;

            if (caretPos.Paragraph != _promptStart.Paragraph)
                return false;

            int offset = _promptStart.GetOffsetToPosition(caretPos);
            return offset >= -1;
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
            Paragraph? targetParagraph;

            // If there is already some output, use the last paragraph instead of making a new one
            if (TerminalBox.Document.Blocks.LastBlock is Paragraph lastParagraph)
            {
                targetParagraph = lastParagraph;
            }
            else
            {
                targetParagraph = new Paragraph { Margin = new Thickness(0) };
                TerminalBox.Document.Blocks.Add(targetParagraph);
            }

            // static prompt text
            var promptRun = new Run(InputLineSyntax) { Foreground = Brushes.LightGreen };
            targetParagraph.Inlines.Add(promptRun);

            // input area
            var inputRun = new Run();
            targetParagraph.Inlines.Add(inputRun);

            _promptParagraph = targetParagraph;
            _promptStart = inputRun.ContentStart;

            // caret goes at the input area
            TerminalBox.CaretPosition = _promptStart;
            TerminalBox.Focus();
            ScrollToEnd();
        }



        #endregion
    }
}
