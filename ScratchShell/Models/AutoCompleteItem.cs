using ScratchShell.Services.Navigation;

namespace ScratchShell.Models;

public class AutoCompleteItem
{
    public string Text { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CompletionType Type { get; set; }
}
