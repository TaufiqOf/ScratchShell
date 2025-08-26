using Renci.SshNet;
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
    private readonly HashSet<string> _commonCommands;
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

        if (string.IsNullOrEmpty(currentLine) || cursorPosition < 0)
        {
            return new AutoCompleteResult();
        }

        // Find the token at cursor position
        var tokenInfo = GetTokenAtCursor(currentLine, cursorPosition);
        if (tokenInfo == null)
        {
            return new AutoCompleteResult();
        }

        var suggestions = new List<CompletionSuggestion>();

        // If it's the first token, provide command completions
        if (tokenInfo.IsFirstToken)
        {
            var commandSuggestions = await GetCommandCompletionsAsync(tokenInfo.Token);
            suggestions.AddRange(commandSuggestions);
        }
        else
        {
            // For subsequent tokens, provide path completions
            var pathSuggestions = await GetPathCompletionsAsync(tokenInfo.Token, workingDirectory);
            suggestions.AddRange(pathSuggestions);
        }

        // Find common prefix
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
        // Don't trigger if line is empty or cursor is at beginning
        if (string.IsNullOrEmpty(currentLine) || cursorPosition <= 0)
            return false;

        // Don't trigger if we're in the middle of a word (unless it's a path)
        if (cursorPosition < currentLine.Length &&
            !char.IsWhiteSpace(currentLine[cursorPosition]) &&
            currentLine[cursorPosition] != '/')
            return false;

        return true;
    }

    private async Task<IEnumerable<CompletionSuggestion>> GetCommandCompletionsAsync(string partialCommand)
    {
        var suggestions = new List<CompletionSuggestion>();

        // Add common commands
        var matchingCommands = _commonCommands
            .Where(cmd => cmd.StartsWith(partialCommand, StringComparison.OrdinalIgnoreCase))
            .Select(cmd => new CompletionSuggestion
            {
                Text = cmd,
                DisplayText = cmd,
                Type = CompletionType.Command,
                Description = GetCommandDescription(cmd)
            });

        suggestions.AddRange(matchingCommands);

        // If SSH client is available, get commands from PATH
        if (_sshClient?.IsConnected == true)
        {
            try
            {
                var pathCommands = await GetPathCommandsAsync(partialCommand);
                suggestions.AddRange(pathCommands);
            }
            catch
            {
                // Ignore errors when getting remote commands
            }
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
            catch
            {
                // Ignore errors when getting remote paths
            }
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
            // Determine the directory to search in
            string searchDir;
            string prefix;
            var command = _sshClient.CreateCommand($"pwd");
            searchDir = await Task.Run(() => command.Execute()); 
            if (partialPath.Contains('/'))
            {
                var lastSlash = partialPath.LastIndexOf('/');
                searchDir = partialPath.Substring(0, lastSlash + 1);
                prefix = partialPath.Substring(lastSlash + 1);

                // Handle absolute vs relative paths
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
            // Use ls command to get directory contents
            command = _sshClient.CreateCommand($"ls -la \"{searchDir}\" 2>/dev/null");
            var result = await Task.Run(() => command.Execute());

            if (command.ExitStatus == 0)
            {
                var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines.Skip(1)) // Skip "total" line
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 9)
                    {
                        var fileName = string.Join(" ", parts.Skip(8));

                        // Skip . and .. entries unless specifically requested
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
        catch
        {
            // Ignore errors
        }

        return suggestions;
    }

    private async Task<IEnumerable<CompletionSuggestion>> GetPathCommandsAsync(string partialCommand)
    {
        if (_sshClient == null || !_sshClient.IsConnected)
            return Enumerable.Empty<CompletionSuggestion>();

        var suggestions = new List<CompletionSuggestion>();

        try
        {
            // Use compgen if available, otherwise fall back to which
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
        catch
        {
            // Ignore errors
        }

        return suggestions;
    }

    private TokenInfo? GetTokenAtCursor(string line, int cursorPosition)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        // Find token boundaries
        var tokens = SplitIntoTokens(line);
        var currentCharIndex = 0;
        var tokenIndex = 0;

        foreach (var token in tokens)
        {
            var tokenStart = currentCharIndex;
            var tokenEnd = currentCharIndex + token.Length;

            if (cursorPosition >= tokenStart && cursorPosition <= tokenEnd)
            {
                return new TokenInfo
                {
                    Token = token,
                    StartIndex = tokenStart,
                    IsFirstToken = tokenIndex == 0
                };
            }

            currentCharIndex = tokenEnd;

            // Skip whitespace
            while (currentCharIndex < line.Length && char.IsWhiteSpace(line[currentCharIndex]))
                currentCharIndex++;

            tokenIndex++;
        }

        // If cursor is at the end, consider it part of a new token
        if (cursorPosition >= line.Length)
        {
            return new TokenInfo
            {
                Token = "",
                StartIndex = line.Length,
                IsFirstToken = tokens.Count == 0
            };
        }

        return null;
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

    private HashSet<string> InitializeCommonCommands()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // File operations
            "ls", "ll", "la", "dir", "pwd", "cd", "mkdir", "rmdir", "rm", "cp", "mv", "ln",
            "chmod", "chown", "chgrp", "find", "locate", "which", "whereis",
            
            // File viewing/editing
            "cat", "less", "more", "head", "tail", "nano", "vim", "vi", "emacs",
            
            // Text processing
            "grep", "awk", "sed", "sort", "uniq", "cut", "tr", "wc",
            
            // System information
            "ps", "top", "htop", "df", "du", "free", "uptime", "whoami", "id", "groups",
            "uname", "lsb_release", "hostname",
            
            // Network
            "ping", "wget", "curl", "ssh", "scp", "rsync", "netstat", "ss",
            
            // Archive operations
            "tar", "gzip", "gunzip", "zip", "unzip",
            
            // Process management
            "jobs", "bg", "fg", "nohup", "kill", "killall", "pgrep", "pkill",
            
            // System control
            "sudo", "su", "systemctl", "service", "mount", "umount",
            
            // Git commands (common in development environments)
            "git", "svn",
            
            // Package management
            "apt", "apt-get", "yum", "dnf", "pip", "npm", "docker"
        };
    }

    private string GetCommandDescription(string command)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ls"] = "List directory contents",
            ["cd"] = "Change directory",
            ["pwd"] = "Print working directory",
            ["mkdir"] = "Create directory",
            ["rm"] = "Remove files or directories",
            ["cp"] = "Copy files or directories",
            ["mv"] = "Move/rename files or directories",
            ["cat"] = "Display file contents",
            ["grep"] = "Search text patterns",
            ["find"] = "Find files and directories",
            ["ps"] = "Show running processes",
            ["top"] = "Display and update running processes",
            ["df"] = "Display filesystem disk space usage",
            ["free"] = "Display memory usage",
            ["ping"] = "Send ICMP echo requests",
            ["ssh"] = "Secure Shell remote login",
            ["git"] = "Version control system",
            ["sudo"] = "Execute commands as another user"
        };

        return descriptions.TryGetValue(command, out var description) ? description : "Command";
    }

    private class TokenInfo
    {
        public string Token { get; set; } = string.Empty;
        public int StartIndex { get; set; }
        public bool IsFirstToken { get; set; }
    }
}