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

    /// <summary>
    /// Categorizes command based on the first word to group similar commands
    /// </summary>
    public string GetCommandCategory()
    {
        if (string.IsNullOrWhiteSpace(Code))
            return "Other";

        var firstWord = Code.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLower();

        return firstWord switch
        {
            "ls" or "ll" or "dir" => "File Listing",
            "cd" or "pwd" => "Navigation",
            "cp" or "mv" or "rm" or "mkdir" or "rmdir" => "File Operations",
            "cat" or "less" or "more" or "head" or "tail" or "grep" => "File Content",
            "ps" or "top" or "kill" or "killall" or "jobs" or "nohup" => "Process Management",
            "df" or "du" or "free" or "uname" or "uptime" or "who" or "whoami" => "System Info",
            "git" => "Git",
            "docker" => "Docker",
            "ssh" or "scp" or "ping" or "wget" or "curl" => "Network",
            "tar" or "zip" or "unzip" or "gzip" => "Archives",
            "chmod" or "chown" or "chgrp" => "Permissions",
            "systemctl" or "service" => "Services",
            "journalctl" or "dmesg" => "Logs",
            "sudo" or "su" => "Admin",
            "find" or "locate" or "which" or "type" => "Search",
            _ => "Other"
        };
    }

    [RelayCommand]
    private Task OnRun()
    {
        ExecuteSnippet?.Invoke(this);
        return Task.CompletedTask;
    }
}