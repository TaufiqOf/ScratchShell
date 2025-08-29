using Renci.SshNet;
using ScratchShell.Models;
using ScratchShell.Services.Navigation;
using Spectre.Console.Cli;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ScratchShell.Services.Terminal;

/// <summary>
/// SSH-specific implementation of terminal autocomplete service
/// </summary>
public class SshAutoCompleteService : ITerminalAutoCompleteService
{
    private readonly SshClient? _sshClient;
    private readonly IPathCompletionService _pathCompletionService;
    private readonly Dictionary<string, Snippet> _commonCommands;
    private string _currentWorkingDirectory = "~";

    public SshAutoCompleteService(SshClient? sshClient, IPathCompletionService pathCompletionService)
    {
        _sshClient = sshClient;
        _pathCompletionService = pathCompletionService;
        _commonCommands = InitializeCommonCommands();
    }

    public async Task<AutoCompleteResult> GetAutoCompleteAsync(string currentLine, int cursorPosition, string workingDirectory)
    {
        _currentWorkingDirectory = workingDirectory;

        var tokenInfo = GetTokenAtCursor(currentLine, cursorPosition);
        if (tokenInfo == null)
        {
            return new AutoCompleteResult();
        }

        var suggestions = new List<CompletionSuggestion>();

        if (tokenInfo.IsFirstToken)
        {
            var commandSuggestions = await GetCommandCompletionsAsync(tokenInfo.Token);
            suggestions.AddRange(commandSuggestions);
        }
        else
        {
            var pathSuggestions = await GetPathCompletionsAsync(tokenInfo.Token, workingDirectory);
            suggestions.AddRange(pathSuggestions);
        }

        var commonPrefix = FindCommonPrefix(suggestions.Select(s => s.Text));

        return new AutoCompleteResult
        {
            Suggestions = suggestions,
            CommonPrefix = commonPrefix,
            ReplacementStartIndex = tokenInfo.StartIndex,
            ReplacementLength = tokenInfo.Token.Length
        };
    }

    public bool ShouldTriggerAutoComplete(string currentLine, int cursorPosition)
    {
        if (string.IsNullOrEmpty(currentLine) || cursorPosition <= 0)
            return false;

        if (cursorPosition < currentLine.Length &&
            !char.IsWhiteSpace(currentLine[cursorPosition]) &&
            currentLine[cursorPosition] != '/')
            return false;

        return true;
    }

    private async Task<IEnumerable<CompletionSuggestion>> GetCommandCompletionsAsync(string partialCommand)
    {
        var suggestions = new List<CompletionSuggestion>();

        var matchingCommands = _commonCommands
            .Where(cmd => cmd.Key.StartsWith(partialCommand, StringComparison.OrdinalIgnoreCase))
            .Select(cmd => new CompletionSuggestion
            {
                Text = cmd.Key,
                DisplayText = cmd.Key,
                Type = CompletionType.Command,
                Description = cmd.Value.Name
            });

        suggestions.AddRange(matchingCommands);

        if (_sshClient?.IsConnected == true)
        {
            try
            {
                var pathCommands = await GetPathCommandsAsync(partialCommand);
                suggestions.AddRange(pathCommands);
            }
            catch { }
        }

        return suggestions.OrderBy(s => s.Text);
    }

    private async Task<IEnumerable<CompletionSuggestion>> GetPathCompletionsAsync(string partialPath, string workingDirectory)
    {
        var suggestions = new List<CompletionSuggestion>();

        if (_sshClient?.IsConnected == true)
        {
            try
            {
                var remotePathSuggestions = await GetRemotePathCompletionsAsync(partialPath, workingDirectory);
                suggestions.AddRange(remotePathSuggestions);
            }
            catch { }
        }

        return suggestions.OrderBy(s => s.Text);
    }

    private async Task<IEnumerable<CompletionSuggestion>> GetRemotePathCompletionsAsync(string partialPath, string workingDirectory)
    {
        if (_sshClient == null || !_sshClient.IsConnected)
            return Enumerable.Empty<CompletionSuggestion>();

        var suggestions = new List<CompletionSuggestion>();

        try
        {
            string searchDir;
            string prefix;
            var command = _sshClient.CreateCommand($"pwd");
            searchDir = await Task.Run(() => command.Execute());
            if (partialPath.Contains('/'))
            {
                var lastSlash = partialPath.LastIndexOf('/');
                searchDir = partialPath.Substring(0, lastSlash + 1);
                prefix = partialPath.Substring(lastSlash + 1);

                if (!searchDir.StartsWith('/'))
                {
                    searchDir = $"{workingDirectory.TrimEnd('/')}/{searchDir}";
                }
            }
            else
            {
                prefix = partialPath;
            }
            searchDir = searchDir.TrimEnd();

            command = _sshClient.CreateCommand($"ls -la \"{searchDir}\" 2>/dev/null");
            var result = await Task.Run(() => command.Execute());

            if (command.ExitStatus == 0)
            {
                var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines.Skip(1))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 9)
                    {
                        var fileName = string.Join(" ", parts.Skip(8));
                        if (fileName == "." || fileName == "..")
                            continue;

                        if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var isDirectory = line.StartsWith('d');
                            var displayName = isDirectory ? $"{fileName}/" : fileName;

                            suggestions.Add(new CompletionSuggestion
                            {
                                Text = fileName,
                                DisplayText = displayName,
                                Type = isDirectory ? CompletionType.Directory : CompletionType.File,
                                Description = isDirectory ? "Directory" : "File"
                            });
                        }
                    }
                }
            }
        }
        catch { }

        return suggestions;
    }

    private async Task<IEnumerable<CompletionSuggestion>> GetPathCommandsAsync(string partialCommand)
    {
        if (_sshClient == null || !_sshClient.IsConnected)
            return Enumerable.Empty<CompletionSuggestion>();

        var suggestions = new List<CompletionSuggestion>();

        try
        {
            var command = _sshClient.CreateCommand($"compgen -c {partialCommand} 2>/dev/null | head -20");
            var result = await Task.Run(() => command.Execute());

            if (command.ExitStatus == 0 && !string.IsNullOrEmpty(result))
            {
                var commands = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var cmd in commands)
                {
                    if (!string.IsNullOrWhiteSpace(cmd))
                    {
                        suggestions.Add(new CompletionSuggestion
                        {
                            Text = cmd.Trim(),
                            DisplayText = cmd.Trim(),
                            Type = CompletionType.Command,
                            Description = "Available command"
                        });
                    }
                }
            }
        }
        catch { }

        return suggestions;
    }

    private TokenInfo? GetTokenAtCursor(string line, int cursorPosition)
    {
        if (line == null)
            return null;

        if (cursorPosition < 0) cursorPosition = 0;
        if (cursorPosition > line.Length) cursorPosition = line.Length;

        // If cursor is on a whitespace (or end) move left one char to pick previous token if any
        int inspectPos = cursorPosition;
        if (inspectPos > 0 && (inspectPos == line.Length || char.IsWhiteSpace(line[inspectPos])))
        {
            if (!char.IsWhiteSpace(line[inspectPos - 1]))
                inspectPos--;
        }

        // If still whitespace -> new empty token starting at cursorPosition
        if (inspectPos == line.Length || char.IsWhiteSpace(line[inspectPos]))
        {
            bool first = string.IsNullOrWhiteSpace(line);
            // Or everything before cursor is whitespace
            if (!first)
                first = line.Take(cursorPosition).All(char.IsWhiteSpace);
            return new TokenInfo { Token = string.Empty, StartIndex = cursorPosition, IsFirstToken = first };
        }

        // Find start
        int start = inspectPos;
        while (start > 0 && !char.IsWhiteSpace(line[start - 1])) start--;
        // Find end
        int end = inspectPos;
        while (end < line.Length && !char.IsWhiteSpace(line[end])) end++;

        string token = line[start..end];

        bool isFirstToken = line.Take(start).All(char.IsWhiteSpace);

        return new TokenInfo
        {
            Token = token,
            StartIndex = start,
            IsFirstToken = isFirstToken
        };
    }

    private List<string> SplitIntoTokens(string line)
    {
        var tokens = new List<string>();
        var currentToken = new System.Text.StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (!inQuotes && (c == '"' || c == '\''))
            {
                inQuotes = true;
                quoteChar = c;
                currentToken.Append(c);
            }
            else if (inQuotes && c == quoteChar)
            {
                inQuotes = false;
                currentToken.Append(c);
            }
            else if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (currentToken.Length > 0)
                {
                    tokens.Add(currentToken.ToString());
                    currentToken.Clear();
                }
            }
            else
            {
                currentToken.Append(c);
            }
        }

        if (currentToken.Length > 0)
        {
            tokens.Add(currentToken.ToString());
        }

        return tokens;
    }

    private string FindCommonPrefix(IEnumerable<string> strings)
    {
        var stringList = strings.ToList();
        if (!stringList.Any())
            return string.Empty;
        if (stringList.Count == 1)
            return stringList[0];

        var minLength = stringList.Min(s => s.Length);
        var commonPrefix = new System.Text.StringBuilder();
        for (int i = 0; i < minLength; i++)
        {
            var c = stringList[0][i];
            if (stringList.All(s => s[i] == c))
            {
                commonPrefix.Append(c);
            }
            else
            {
                break;
            }
        }
        return commonPrefix.ToString();
    }

    private Dictionary<string, Snippet> InitializeCommonCommands()
    {
        return SnippetManager.Snippets.ToDictionary(q => q.Code, q => q);
    }

    private class TokenInfo
    {
        public string Token { get; set; } = string.Empty;
        public int StartIndex { get; set; }
        public bool IsFirstToken { get; set; }
    }
}