using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ScratchShell.UserControls.TerminalControl
{
    public static class AnsiColorHelper
    {
        public static Color Ansi256ToRgb(int code)
        {
            if (code >= 0 && code <= 15)
            {
                Color[] basicColors = {
                Color.FromRgb(0, 0, 0), Color.FromRgb(128, 0, 0),
                Color.FromRgb(0, 128, 0), Color.FromRgb(128, 128, 0),
                Color.FromRgb(0, 0, 128), Color.FromRgb(128, 0, 128),
                Color.FromRgb(0, 128, 128), Color.FromRgb(192, 192, 192),
                Color.FromRgb(128, 128, 128), Color.FromRgb(255, 0, 0),
                Color.FromRgb(0, 255, 0), Color.FromRgb(255, 255, 0),
                Color.FromRgb(0, 0, 255), Color.FromRgb(255, 0, 255),
                Color.FromRgb(0, 255, 255), Color.FromRgb(255, 255, 255)
            };
                return basicColors[code];
            }
            else if (code >= 16 && code <= 231)
            {
                int index = code - 16;
                int r = index / 36 % 6;
                int g = index / 6 % 6;
                int b = index % 6;
                return Color.FromRgb((byte)(r * 51), (byte)(g * 51), (byte)(b * 51));
            }
            else if (code >= 232 && code <= 255)
            {
                int level = (code - 232) * 10 + 8;
                return Color.FromRgb((byte)level, (byte)level, (byte)level);
            }

            return Colors.White;
        }
    }

}
