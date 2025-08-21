using Newtonsoft.Json;
using ScratchShell.Models;
using ScratchShell.Properties;
using System.Net.Http;

namespace ScratchShell.Services
{
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
}