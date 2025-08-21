using ScratchShell.Models;
using System.ComponentModel;
using Wpf.Ui;

namespace ScratchShell.ViewModels.Models;

public partial class SnippetViewModel : ObservableValidator, IDataErrorInfo
{
    public delegate Task SnippetHandler(SnippetViewModel? snippet);

    public event SnippetHandler ExecuteSnippet;

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private bool _isSystemSnippet = false;

    public IContentDialogService ContentDialogService { get; }

    // Validation properties
    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            switch (columnName)
            {
                case nameof(Name):
                    return ValidateName();

                case nameof(Code):
                    return ValidateCode();

                default:
                    return string.Empty;
            }
        }
    }

    public bool IsValid
    {
        get
        {
            return string.IsNullOrEmpty(ValidateName()) &&
                   string.IsNullOrEmpty(ValidateCode());
        }
    }

    private string ValidateName()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "Snippet name is required.";

        if (Name.Length > 100)
            return "Snippet name cannot exceed 100 characters.";

        // Check for invalid characters
        var invalidChars = new char[] { '<', '>', ':', '"', '|', '?', '*', '\\', '/' };
        if (Name.IndexOfAny(invalidChars) >= 0)
            return "Snippet name contains invalid characters.";

        return string.Empty;
    }

    private string ValidateCode()
    {
        if (string.IsNullOrWhiteSpace(Code))
            return "Snippet code is required.";

        if (Code.Length > 10000)
            return "Snippet code cannot exceed 10,000 characters.";

        return string.Empty;
    }

    public SnippetViewModel(IContentDialogService contentDialogService)
    {
        ContentDialogService = contentDialogService;
        Id = Guid.NewGuid().ToString();
    }

    public SnippetViewModel(Snippet snippet, IContentDialogService contentDialogService)
    {
        Id = snippet.Id ?? Guid.NewGuid().ToString();
        Name = snippet.Name ?? string.Empty;
        Code = snippet.Code ?? string.Empty;
        IsSystemSnippet = snippet.IsSystemSnippet;
        ContentDialogService = contentDialogService;
    }

    public SnippetViewModel(SnippetViewModel snippet, IContentDialogService contentDialogService)
    {
        Id = snippet.Id;
        Name = snippet.Name;
        Code = snippet.Code;
        IsSystemSnippet = snippet.IsSystemSnippet;
        ContentDialogService = contentDialogService;
    }

    internal Snippet ToSnippet(bool setNewId = false)
    {
        var id = Id;
        if (setNewId)
        {
            id = Guid.NewGuid().ToString();
        }

        var snippet = new Snippet
        {
            Id = id,
            Name = Name,
            Code = Code,
            IsSystemSnippet = IsSystemSnippet
        };
        return snippet;
    }

    [RelayCommand]
    private Task OnRun()
    {
        ExecuteSnippet?.Invoke(this);
        return Task.CompletedTask;
    }
}