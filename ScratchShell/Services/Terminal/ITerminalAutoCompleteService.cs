using System.Collections.Generic;
using System.Threading.Tasks;
using ScratchShell.Services.Navigation;

namespace ScratchShell.Services.Terminal;

/// <summary>
/// Service interface for providing terminal autocomplete functionality
/// </summary>
public interface ITerminalAutoCompleteService
{
    /// <summary>
    /// Gets autocomplete suggestions for the current terminal input
    /// </summary>
    /// <param name="currentLine">The current input line</param>
    /// <param name="cursorPosition">Position of cursor in the line</param>
    /// <param name="workingDirectory">Current working directory</param>
    /// <returns>Autocomplete result with suggestions</returns>
    Task<AutoCompleteResult> GetAutoCompleteAsync(string currentLine, int cursorPosition, string workingDirectory);

    /// <summary>
    /// Determines if autocomplete should be triggered for the given context
    /// </summary>
    /// <param name="currentLine">The current input line</param>
    /// <param name="cursorPosition">Position of cursor in the line</param>
    /// <returns>True if autocomplete should be triggered</returns>
    bool ShouldTriggerAutoComplete(string currentLine, int cursorPosition);
}

/// <summary>
/// Result of an autocomplete operation
/// </summary>
public class AutoCompleteResult
{
    public IEnumerable<CompletionSuggestion> Suggestions { get; set; } = new List<CompletionSuggestion>();
    public bool HasSuggestions => Suggestions.Any();
    public string CommonPrefix { get; set; } = string.Empty;
    public int ReplacementStartIndex { get; set; }
    public int ReplacementLength { get; set; }
}