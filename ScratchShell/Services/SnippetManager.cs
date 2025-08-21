using Newtonsoft.Json;
using ScratchShell.Models;
using ScratchShell.Properties;
using System.Net.Http;

namespace ScratchShell.Services;

internal static class SnippetManager
{
    internal delegate Task SnippetDelegate(Snippet? server);

    internal delegate Task MangerDelegate();

    internal static event SnippetDelegate? OnSnippetAdded;

    internal static event SnippetDelegate? OnSnippetRemoved;

    internal static event SnippetDelegate? OnSnippetEdited;

    internal static event SnippetDelegate? OnSnippetSelected;

    internal static event MangerDelegate? OnSnippetInitialized;

    internal static List<Snippet> _snippets { get; private set; } = new List<Snippet>();
    internal static IReadOnlyList<Snippet> Snippets => _snippets.AsReadOnly();
    internal static Server? SelectedSnippet { get; private set; }
    internal static bool NeedsCloudRestore { get; set; }

    internal static void InitializeSnippets(List<Snippet> servers)
    {
        _snippets.Clear();
        _snippets.AddRange(servers);
        SelectedSnippet = null;

        // If we successfully restored servers, clear the restore flag
        NeedsCloudRestore = false;
        OnSnippetInitialized?.Invoke();
        System.Diagnostics.Debug.WriteLine($"Initialized {servers.Count} servers from cloud sync");
        LoadSystemSnippet();
    }

    private static void LoadSystemSnippet()
    {
        // File and Directory Operations
        _snippets.Add(new Snippet
        {
            Name = "List Directories",
            Code = "ls -la",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "List Files by Size",
            Code = "ls -lSh",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "List Files by Date",
            Code = "ls -lt",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Show Hidden Files",
            Code = "ls -la | grep '^\\.'",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Current Directory",
            Code = "pwd",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Create Directory",
            Code = "mkdir -p ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Remove Directory",
            Code = "rm -rf ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Copy Files",
            Code = "cp -r ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Move/Rename Files",
            Code = "mv ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Find Files",
            Code = "find . -name \"*\"",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Find Files by Type",
            Code = "find . -type f -name \"*.txt\"",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Find Large Files",
            Code = "find . -type f -size +100M",
            IsSystemSnippet = true
        });

        // File Content Operations
        _snippets.Add(new Snippet
        {
            Name = "View File Content",
            Code = "cat ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "View File with Line Numbers",
            Code = "cat -n ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "View File Head",
            Code = "head -20 ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "View File Tail",
            Code = "tail -20 ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Follow Log File",
            Code = "tail -f ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Search in Files",
            Code = "grep -r \"pattern\" .",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Search Case Insensitive",
            Code = "grep -ri \"pattern\" .",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Count Lines in File",
            Code = "wc -l ",
            IsSystemSnippet = true
        });

        // System Information
        _snippets.Add(new Snippet
        {
            Name = "System Information",
            Code = "uname -a",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Disk Usage",
            Code = "df -h",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Directory Size",
            Code = "du -sh *",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Memory Usage",
            Code = "free -h",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "CPU Information",
            Code = "lscpu",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Uptime",
            Code = "uptime",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Who is Logged In",
            Code = "who",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Current User",
            Code = "whoami",
            IsSystemSnippet = true
        });

        // Process Management
        _snippets.Add(new Snippet
        {
            Name = "List Processes",
            Code = "ps aux",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Top Processes",
            Code = "top",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Process Tree",
            Code = "pstree",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Kill Process by PID",
            Code = "kill ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Kill Process by Name",
            Code = "killall ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Background Job",
            Code = "nohup  &",
            IsSystemSnippet = true
        });

        // Network Operations
        _snippets.Add(new Snippet
        {
            Name = "Network Interfaces",
            Code = "ip addr show",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Ping Host",
            Code = "ping -c 4 ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Download File",
            Code = "wget ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Download with Curl",
            Code = "curl -O ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Check Port",
            Code = "netstat -tuln | grep ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "SSH to Server",
            Code = "ssh user@hostname",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "SCP Copy File",
            Code = "scp file user@host:/path/",
            IsSystemSnippet = true
        });

        // Archive Operations
        _snippets.Add(new Snippet
        {
            Name = "Create Tar Archive",
            Code = "tar -czf archive.tar.gz ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Extract Tar Archive",
            Code = "tar -xzf ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Create Zip Archive",
            Code = "zip -r archive.zip ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Extract Zip Archive",
            Code = "unzip ",
            IsSystemSnippet = true
        });

        // Permissions and Ownership
        _snippets.Add(new Snippet
        {
            Name = "Change Permissions",
            Code = "chmod 755 ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Make Executable",
            Code = "chmod +x ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Change Owner",
            Code = "chown user:group ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Recursive Permissions",
            Code = "chmod -R 755 ",
            IsSystemSnippet = true
        });

        // Text Processing
        _snippets.Add(new Snippet
        {
            Name = "Sort File",
            Code = "sort ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Unique Lines",
            Code = "sort  | uniq",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Replace Text",
            Code = "sed 's/old/new/g' ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Cut Columns",
            Code = "cut -d',' -f1 ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Awk Pattern",
            Code = "awk '{print $1}' ",
            IsSystemSnippet = true
        });

        // System Administration
        _snippets.Add(new Snippet
        {
            Name = "Sudo Command",
            Code = "sudo ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Switch User",
            Code = "su - ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Crontab Edit",
            Code = "crontab -e",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "List Cron Jobs",
            Code = "crontab -l",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Service Status",
            Code = "systemctl status ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Start Service",
            Code = "systemctl start ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Stop Service",
            Code = "systemctl stop ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Restart Service",
            Code = "systemctl restart ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Enable Service",
            Code = "systemctl enable ",
            IsSystemSnippet = true
        });

        // Log Analysis
        _snippets.Add(new Snippet
        {
            Name = "System Logs",
            Code = "journalctl -f",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Service Logs",
            Code = "journalctl -u ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Boot Logs",
            Code = "journalctl -b",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Kernel Messages",
            Code = "dmesg | tail",
            IsSystemSnippet = true
        });

        // Development Tools
        _snippets.Add(new Snippet
        {
            Name = "Git Status",
            Code = "git status",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Git Add All",
            Code = "git add .",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Git Commit",
            Code = "git commit -m \"\"",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Git Push",
            Code = "git push origin main",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Git Pull",
            Code = "git pull",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Git Branch",
            Code = "git branch -a",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Docker Images",
            Code = "docker images",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Docker Containers",
            Code = "docker ps -a",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Docker Run",
            Code = "docker run -it ",
            IsSystemSnippet = true
        });

        // Environment and Variables
        _snippets.Add(new Snippet
        {
            Name = "Environment Variables",
            Code = "env",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Export Variable",
            Code = "export VAR=value",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "PATH Variable",
            Code = "echo $PATH",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "History",
            Code = "history",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Clear Screen",
            Code = "clear",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Manual Page",
            Code = "man ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Which Command",
            Code = "which ",
            IsSystemSnippet = true
        });

        _snippets.Add(new Snippet
        {
            Name = "Command Type",
            Code = "type ",
            IsSystemSnippet = true
        });
    }

    internal static async Task Add(Snippet snippet)
    {
        if (snippet == null)
            throw new ArgumentNullException(nameof(snippet));
        _snippets.Add(snippet);
        SaveSettings();
        OnSnippetAdded?.Invoke(snippet);
        await Task.CompletedTask;
    }

    internal static async Task Remove(Snippet snippet)
    {
        if (snippet == null)
            throw new ArgumentNullException(nameof(snippet));
        var existingSnippet = _snippets.FirstOrDefault(s => s.Id == snippet.Id);
        _snippets.Remove(existingSnippet);
        SaveSettings();
        OnSnippetRemoved?.Invoke(existingSnippet);
        await Task.CompletedTask;
    }

    internal static async Task<Snippet?> Edit(Snippet snippet)
    {
        if (snippet == null)
            throw new ArgumentNullException(nameof(snippet));
        var existingSnippet = _snippets.FirstOrDefault(s => s.Id == snippet.Id);
        if (existingSnippet != null)
        {
            existingSnippet.Name = snippet.Name;
            existingSnippet.Code = snippet.Code;
        }
        SaveSettings();
        OnSnippetEdited?.Invoke(existingSnippet);
        return await Task.FromResult(existingSnippet);
    }

    private static async void SaveSettings()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_snippets.ToList());
            var encrypted = EncryptionHelper.Encrypt(json);
            Settings.Default.Snippets = encrypted;
            Settings.Default.Save();
            var _cloudSyncService = new CloudSyncService(new HttpClient());
            await UserSettingsService.TriggerCloudSyncIfEnabled();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving servers: {ex.Message}");
        }
    }

    internal static void ClearServers()
    {
        _snippets.Clear();
        SelectedSnippet = null;
    }
}