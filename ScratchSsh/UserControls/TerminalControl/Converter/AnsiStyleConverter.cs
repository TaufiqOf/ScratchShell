using System.Windows.Media;

namespace ScratchShell.UserControls.TerminalControl.Converter;

public static class AnsiStyleConverter
{
    public static void ApplyCode(ref AnsiStyle style, List<string> codes, ref int index)
    {
        if (!int.TryParse(codes[index].TrimStart('?'), out int code))
            return;

        switch (code)
        {
            case 0:
                style.Reset();
                break;
            case 1:
                style.FontWeight = FontWeights.Bold;
                break;
            case 3:
                style.FontStyle = FontStyles.Italic;
                break;
            case 4:
                style.TextDecorations.Add(TextDecorations.Underline[0]);
                break;
            case 9:
                style.TextDecorations.Add(TextDecorations.Strikethrough[0]);
                break;

            case 23:
                style.FontStyle = FontStyles.Normal;
                break;
            // Standard Foreground
            case 30: style.Foreground = Brushes.Black; break;
            case 31: style.Foreground = Brushes.Red; break;
            case 32: style.Foreground = Brushes.Green; break;
            case 33: style.Foreground = Brushes.Yellow; break;
            case 34: style.Foreground = Brushes.Blue; break;
            case 35: style.Foreground = Brushes.Magenta; break;
            case 36: style.Foreground = Brushes.Cyan; break;
            case 37: style.Foreground = Brushes.White; break;

            // Standard Background
            case 40: style.Background = Brushes.Black; break;
            case 41: style.Background = Brushes.Red; break;
            case 42: style.Background = Brushes.Green; break;
            case 43: style.Background = Brushes.Yellow; break;
            case 44: style.Background = Brushes.Blue; break;
            case 45: style.Background = Brushes.Magenta; break;
            case 46: style.Background = Brushes.Cyan; break;
            case 47: style.Background = Brushes.White; break;

            // Bright Foreground
            case 90: style.Foreground = Brushes.Gray; break;
            case 91: style.Foreground = Brushes.LightCoral; break;
            case 92: style.Foreground = Brushes.LightGreen; break;
            case 93: style.Foreground = Brushes.LightGoldenrodYellow; break;
            case 94: style.Foreground = Brushes.LightBlue; break;
            case 95: style.Foreground = Brushes.Plum; break;
            case 96: style.Foreground = Brushes.LightCyan; break;
            case 97: style.Foreground = Brushes.WhiteSmoke; break;

            // Bright Background
            case 100: style.Background = Brushes.Gray; break;
            case 101: style.Background = Brushes.LightCoral; break;
            case 102: style.Background = Brushes.LightGreen; break;
            case 103: style.Background = Brushes.LightGoldenrodYellow; break;
            case 104: style.Background = Brushes.LightBlue; break;
            case 105: style.Background = Brushes.Plum; break;
            case 106: style.Background = Brushes.LightCyan; break;
            case 107: style.Background = Brushes.WhiteSmoke; break;

            // 256-color and truecolor
            case 38:
            case 48:
                bool isForeground = code == 38;
                if (codes.Count > index + 2 && codes[index + 1] == "5")
                {
                    if (int.TryParse(codes[index + 2], out int colorCode))
                    {
                        var brush = new SolidColorBrush(AnsiColorHelper.Ansi256ToRgb(colorCode));
                        if (isForeground)
                            style.Foreground = brush;
                        else
                            style.Background = brush;
                    }
                    index += 2;
                }
                else if (codes.Count > index + 4 && codes[index + 1] == "2")
                {
                    if (int.TryParse(codes[index + 2], out int r) &&
                        int.TryParse(codes[index + 3], out int g) &&
                        int.TryParse(codes[index + 4], out int b))
                    {
                        var brush = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
                        if (isForeground)
                            style.Foreground = brush;
                        else
                            style.Background = brush;
                    }
                    index += 4;
                }
                break;
        }
    }
}
