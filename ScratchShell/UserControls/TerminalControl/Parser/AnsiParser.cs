using ScratchShell.UserControls.TerminalControl.Converter;
using System.Text.RegularExpressions;

namespace ScratchShell.UserControls.TerminalControl.Parser;

public class AnsiParser
{
    private static readonly Regex AnsiRegex = new(@"\x1B\[(?<code>[0-9;?]*)(?<command>[a-zA-Z])", RegexOptions.Compiled);

    public List<AnsiSegment> Parse(string input)
    {
        var segments = new List<AnsiSegment>();
        var matches = AnsiRegex.Matches(input);
        int lastIndex = 0;
        var currentStyle = new AnsiStyle();

        foreach (Match match in matches)
        {
            int index = match.Index;
            string before = input.Substring(lastIndex, index - lastIndex);
            if (!string.IsNullOrEmpty(before))
            {
                segments.Add(new AnsiSegment(before, currentStyle));
            }

            var codeGroup = match.Groups["code"].Value;
            var command = match.Groups["command"].Value;

            if (command == "m")
            {
                var codes = codeGroup.Split(';').ToList();
                for (int i = 0; i < codes.Count; i++)
                {
                    AnsiStyleConverter.ApplyCode(ref currentStyle, codes, ref i);
                }
            }

            lastIndex = index + match.Length;
        }

        if (lastIndex < input.Length)
        {
            var remaining = input.Substring(lastIndex);
            segments.Add(new AnsiSegment(remaining, currentStyle));
        }

        return segments;
    }
}