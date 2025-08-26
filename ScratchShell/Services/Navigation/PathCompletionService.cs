using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ScratchShell.Services.Navigation;

/// <summary>
/// Basic implementation of path completion service
/// </summary>
public class PathCompletionService : IPathCompletionService
{
    public async Task<IEnumerable<CompletionSuggestion>> GetCompletionsAsync(string input, int cursorPosition)
    {
        if (string.IsNullOrEmpty(input) || cursorPosition < 0)
            return Enumerable.Empty<CompletionSuggestion>();

        // Extract the word at cursor position
        var wordInfo = ExtractWordAtCursor(input, cursorPosition);
        if (wordInfo == null)
            return Enumerable.Empty<CompletionSuggestion>();

        // Determine if this looks like a path or command
        if (wordInfo.Word.Contains('/') || wordInfo.Word.StartsWith("./") || wordInfo.Word.StartsWith("../"))
        {
            return await GetPathCompletionsAsync(wordInfo.Word, ".");
        }

        // Default to command completion
        return await GetCommandCompletionsAsync(wordInfo.Word);
    }

    public async Task<IEnumerable<CompletionSuggestion>> GetPathCompletionsAsync(string partialPath, string currentDirectory)
    {
        // This is a basic implementation - in a real scenario, this would
        // interact with the file system or remote system to get actual paths
        await Task.CompletedTask;
        
        var suggestions = new List<CompletionSuggestion>();
        
        // Add some common directory suggestions
        if (string.IsNullOrEmpty(partialPath) || "home".StartsWith(partialPath, StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add(new CompletionSuggestion
            {
                Text = "home/",
                DisplayText = "home/",
                Type = CompletionType.Directory,
                Description = "Home directory"
            });
        }

        if (string.IsNullOrEmpty(partialPath) || "tmp".StartsWith(partialPath, StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add(new CompletionSuggestion
            {
                Text = "tmp/",
                DisplayText = "tmp/",
                Type = CompletionType.Directory,
                Description = "Temporary directory"
            });
        }

        if (string.IsNullOrEmpty(partialPath) || "var".StartsWith(partialPath, StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add(new CompletionSuggestion
            {
                Text = "var/",
                DisplayText = "var/",
                Type = CompletionType.Directory,
                Description = "Variable data directory"
            });
        }

        return suggestions;
    }

    public async Task<IEnumerable<CompletionSuggestion>> GetCommandCompletionsAsync(string partialCommand)
    {
        await Task.CompletedTask;
        
        var commonCommands = new[]
        {
            "ls", "cd", "pwd", "mkdir", "rm", "cp", "mv", "cat", "grep", "find",
            "ps", "top", "df", "free", "ping", "ssh", "git", "nano", "vim"
        };

        return commonCommands
            .Where(cmd => cmd.StartsWith(partialCommand, StringComparison.OrdinalIgnoreCase))
            .Select(cmd => new CompletionSuggestion
            {
                Text = cmd,
                DisplayText = cmd,
                Type = CompletionType.Command,
                Description = "Command"
            });
    }

    private WordInfo? ExtractWordAtCursor(string input, int cursorPosition)
    {
        if (cursorPosition > input.Length)
            return null;

        int start = cursorPosition;
        int end = cursorPosition;

        // Find start of word
        while (start > 0 && !char.IsWhiteSpace(input[start - 1]))
            start--;

        // Find end of word
        while (end < input.Length && !char.IsWhiteSpace(input[end]))
            end++;

        if (start == end)
            return null;

        return new WordInfo
        {
            Word = input.Substring(start, end - start),
            StartIndex = start,
            EndIndex = end
        };
    }

    private class WordInfo
    {
        public string Word { get; set; } = string.Empty;
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
    }
}