using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScratchShell.Services.Navigation;

/// <summary>
/// Service interface for providing path and command completion suggestions
/// </summary>
public interface IPathCompletionService
{
    /// <summary>
    /// Gets completion suggestions for the given input context
    /// </summary>
    /// <param name="input">The current input text</param>
    /// <param name="cursorPosition">Position of cursor in the input</param>
    /// <returns>List of completion suggestions</returns>
    Task<IEnumerable<CompletionSuggestion>> GetCompletionsAsync(string input, int cursorPosition);

    /// <summary>
    /// Gets completion suggestions for file/directory paths
    /// </summary>
    /// <param name="partialPath">The partial path to complete</param>
    /// <param name="currentDirectory">Current working directory</param>
    /// <returns>List of path completion suggestions</returns>
    Task<IEnumerable<CompletionSuggestion>> GetPathCompletionsAsync(string partialPath, string currentDirectory);

    /// <summary>
    /// Gets completion suggestions for commands
    /// </summary>
    /// <param name="partialCommand">The partial command to complete</param>
    /// <returns>List of command completion suggestions</returns>
    Task<IEnumerable<CompletionSuggestion>> GetCommandCompletionsAsync(string partialCommand);
}

/// <summary>
/// Represents a completion suggestion
/// </summary>
public class CompletionSuggestion
{
    public string Text { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public CompletionType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public int ReplacementStartIndex { get; set; }
    public int ReplacementLength { get; set; }
}

/// <summary>
/// Type of completion suggestion
/// </summary>
public enum CompletionType
{
    File,
    Directory,
    Command,
    Parameter,
    Variable
}