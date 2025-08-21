using Newtonsoft.Json;
using ScratchShell.Enums;
using ScratchShell.Models;
using ScratchShell.Properties;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScratchShell.Services
{
    public class CloudSyncService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private static readonly string DeviceId = GetDeviceId();
        private static readonly string DeviceName = Environment.MachineName;

        // Track recent sync operations to prevent false conflicts
        private DateTime _lastUploadTime = DateTime.MinValue;

        private readonly TimeSpan _recentSyncWindow = TimeSpan.FromMinutes(2);

        public CloudSyncService(HttpClient httpClient, string baseUrl = "https://localhost:7110")
        {
            _httpClient = httpClient;
            _baseUrl = baseUrl;
        }

        public event EventHandler<SyncStatusEventArgs>? SyncStatusChanged;

        public event EventHandler<ConflictDetectedEventArgs>? ConflictDetected;

        /// <summary>
        /// Syncs local settings to the cloud
        /// </summary>
        public async Task<SyncResult> SyncToCloudAsync(bool forceOverwrite = false)
        {
            try
            {
                // Ensure user is authenticated
                if (!AuthenticationService.IsTokenValid())
                {
                    return new SyncResult
                    {
                        IsSuccess = false,
                        Message = "User not authenticated"
                    };
                }

                // Check if encryption keys are available for cloud sync
                if (!EncryptionHelper.IsEncryptionAvailable)
                {
                    // For auto-login scenarios, try to prompt for password
                    var username = UserSettingsService.GetStoredUsername();
                    if (!string.IsNullOrEmpty(username))
                    {
                        return new SyncResult
                        {
                            IsSuccess = false,
                            Message = "Cloud encryption keys not available. Please re-enter your password to enable cloud sync.",
                            RequiresPasswordReentry = true
                        };
                    }
                    else
                    {
                        return new SyncResult
                        {
                            IsSuccess = false,
                            Message = "Cloud encryption keys not available. Please login again to enable cloud sync."
                        };
                    }
                }

                // Set authorization header
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AuthenticationService.Token);

                // Gather current local settings
                var localSettings = GatherLocalSettings();

                var syncRequest = new SyncSettingsRequest
                {
                    Settings = localSettings,
                    ForceOverwrite = forceOverwrite
                };

                var json = System.Text.Json.JsonSerializer.Serialize(syncRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/settingssync/sync", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var syncResponse = System.Text.Json.JsonSerializer.Deserialize<SyncSettingsResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (syncResponse?.IsSuccess == true)
                    {
                        if (syncResponse.HasConflict)
                        {
                            // Notify about conflict
                            ConflictDetected?.Invoke(this, new ConflictDetectedEventArgs
                            {
                                ServerSettings = syncResponse.Settings,
                                LocalSettings = localSettings,
                                ServerLastSynced = syncResponse.ServerLastSyncedAt,
                                ClientLastSynced = syncResponse.ClientLastSyncedAt
                            });

                            return new SyncResult
                            {
                                IsSuccess = false,
                                Message = "Sync conflict detected",
                                HasConflict = true,
                                ServerSettings = syncResponse.Settings
                            };
                        }

                        // Update local sync timestamp
                        UpdateLocalSyncTimestamp(syncResponse.ServerLastSyncedAt ?? DateTime.UtcNow);

                        // Track this upload to prevent false conflicts
                        _lastUploadTime = DateTime.UtcNow;

                        SyncStatusChanged?.Invoke(this, new SyncStatusEventArgs
                        {
                            Status = SyncStatus.UploadCompleted,
                            Message = "Settings synced to cloud successfully"
                        });

                        return new SyncResult
                        {
                            IsSuccess = true,
                            Message = "Settings synced to cloud successfully"
                        };
                    }
                    else
                    {
                        return new SyncResult
                        {
                            IsSuccess = false,
                            Message = syncResponse?.Message ?? "Sync failed"
                        };
                    }
                }
                else
                {
                    return new SyncResult
                    {
                        IsSuccess = false,
                        Message = $"Sync failed: {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new SyncResult
                {
                    IsSuccess = false,
                    Message = $"Sync error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Downloads settings from the cloud and applies them locally
        /// </summary>
        public async Task<SyncResult> SyncFromCloudAsync(bool forceOverwrite = false)
        {
            try
            {
                // Ensure user is authenticated
                if (!AuthenticationService.IsTokenValid())
                {
                    return new SyncResult
                    {
                        IsSuccess = false,
                        Message = "User not authenticated"
                    };
                }

                // Check if encryption keys are available for cloud sync
                if (!EncryptionHelper.IsEncryptionAvailable)
                {
                    // For auto-login scenarios, try to prompt for password
                    var username = UserSettingsService.GetStoredUsername();
                    if (!string.IsNullOrEmpty(username))
                    {
                        return new SyncResult
                        {
                            IsSuccess = false,
                            Message = "Cloud encryption keys not available. Please re-enter your password to enable cloud sync.",
                            RequiresPasswordReentry = true
                        };
                    }
                    else
                    {
                        return new SyncResult
                        {
                            IsSuccess = false,
                            Message = "Cloud encryption keys not available. Please login again to enable cloud sync."
                        };
                    }
                }

                // Set authorization header
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AuthenticationService.Token);

                var response = await _httpClient.GetAsync($"{_baseUrl}/api/settingssync");

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var getResponse = System.Text.Json.JsonSerializer.Deserialize<GetSettingsResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (getResponse?.IsSuccess == true)
                    {
                        if (!getResponse.HasSettings)
                        {
                            return new SyncResult
                            {
                                IsSuccess = true,
                                Message = "No cloud settings found"
                            };
                        }

                        var cloudSettings = getResponse.Settings;
                        if (cloudSettings != null)
                        {
                            // Check for conflicts if not forcing overwrite
                            if (!forceOverwrite && HasLocalChanges(cloudSettings.LastSyncedAt))
                            {
                                // Check if we recently uploaded to cloud - if so, skip conflict detection
                                var timeSinceLastUpload = DateTime.UtcNow - _lastUploadTime;
                                if (timeSinceLastUpload <= _recentSyncWindow)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Skipping conflict check - recent upload detected ({timeSinceLastUpload.TotalSeconds:F1}s ago)");
                                }
                                else
                                {
                                    ConflictDetected?.Invoke(this, new ConflictDetectedEventArgs
                                    {
                                        ServerSettings = cloudSettings,
                                        LocalSettings = GatherLocalSettings(),
                                        ServerLastSynced = cloudSettings.LastSyncedAt,
                                        ClientLastSynced = GetLocalSyncTimestamp()
                                    });

                                    return new SyncResult
                                    {
                                        IsSuccess = false,
                                        Message = "Local changes detected. Resolve conflicts first.",
                                        HasConflict = true,
                                        ServerSettings = cloudSettings
                                    };
                                }
                            }

                            // Apply cloud settings locally
                            ApplyCloudSettingsLocally(cloudSettings);

                            SyncStatusChanged?.Invoke(this, new SyncStatusEventArgs
                            {
                                Status = SyncStatus.DownloadCompleted,
                                Message = "Settings downloaded from cloud successfully"
                            });

                            return new SyncResult
                            {
                                IsSuccess = true,
                                Message = "Settings downloaded from cloud successfully"
                            };
                        }
                    }

                    return new SyncResult
                    {
                        IsSuccess = false,
                        Message = getResponse?.Message ?? "Failed to get cloud settings"
                    };
                }
                else
                {
                    return new SyncResult
                    {
                        IsSuccess = false,
                        Message = $"Download failed: {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new SyncResult
                {
                    IsSuccess = false,
                    Message = $"Download error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Resolves sync conflicts by choosing local or server settings
        /// </summary>
        public async Task<SyncResult> ResolveConflictAsync(ConflictResolution resolution, UserSettingsData? customSettings = null)
        {
            try
            {
                UserSettingsData settingsToUse;

                switch (resolution)
                {
                    case ConflictResolution.UseLocal:
                        settingsToUse = GatherLocalSettings();
                        return await SyncToCloudAsync(forceOverwrite: true);

                    case ConflictResolution.UseServer:
                        return await SyncFromCloudAsync(forceOverwrite: true);

                    case ConflictResolution.UseCustom:
                        if (customSettings == null)
                        {
                            return new SyncResult
                            {
                                IsSuccess = false,
                                Message = "Custom settings not provided"
                            };
                        }
                        settingsToUse = customSettings;
                        ApplyCloudSettingsLocally(settingsToUse);
                        return await SyncToCloudAsync(forceOverwrite: true);

                    default:
                        return new SyncResult
                        {
                            IsSuccess = false,
                            Message = "Invalid resolution option"
                        };
                }
            }
            catch (Exception ex)
            {
                return new SyncResult
                {
                    IsSuccess = false,
                    Message = $"Conflict resolution error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Deletes all cloud settings for the current user
        /// </summary>
        public async Task<SyncResult> DeleteCloudSettingsAsync()
        {
            try
            {
                // Ensure user is authenticated
                if (!AuthenticationService.IsTokenValid())
                {
                    return new SyncResult
                    {
                        IsSuccess = false,
                        Message = "User not authenticated"
                    };
                }

                // Set authorization header
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AuthenticationService.Token);

                var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/settingssync");

                if (response.IsSuccessStatusCode)
                {
                    // Clear local sync timestamp completely to reset sync state
                    ClearLocalSyncTimestamp();

                    // Reset upload tracking since we deleted cloud data
                    _lastUploadTime = DateTime.MinValue;

                    return new SyncResult
                    {
                        IsSuccess = true,
                        Message = "Cloud settings deleted successfully"
                    };
                }
                else
                {
                    return new SyncResult
                    {
                        IsSuccess = false,
                        Message = $"Delete failed: {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new SyncResult
                {
                    IsSuccess = false,
                    Message = $"Delete error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Clears all local settings (used during logout)
        /// </summary>
        public static void ClearAllLocalSettings()
        {
            Settings.Default.Reset();
            Settings.Default.Save();
            File.Delete(GetSyncFile());
            SecureKeyStore.ResetKey();
            // Also clear servers
            ServerManager.ClearServers();
        }

        /// <summary>
        /// Gathers all current local settings into a transportable format
        /// </summary>
        private UserSettingsData GatherLocalSettings()
        {
            // For cloud sync, we need to decrypt locally encrypted servers and re-encrypt them with cloud keys
            string cloudEncryptedServers = string.Empty;

            if (!string.IsNullOrEmpty(Settings.Default.Servers))
            {
                try
                {
                    // Decrypt using local keys (device-specific)
                    var decryptedServers = EncryptionHelper.Decrypt(Settings.Default.Servers);

                    // Re-encrypt using cloud keys (user-specific)
                    cloudEncryptedServers = EncryptionHelper.Encrypt(decryptedServers);
                }
                catch (CryptographicException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error decrypting servers for cloud sync (key mismatch): {ex.Message}");
                    // Local servers are encrypted with incompatible keys, send empty servers to cloud
                    cloudEncryptedServers = string.Empty;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error preparing servers for cloud sync: {ex.Message}");
                    // If encryption fails, we'll send empty servers rather than risk data corruption
                    cloudEncryptedServers = string.Empty;
                }
            }

            return new UserSettingsData
            {
                CurrentTheme = Settings.Default.CurrentTheme,
                DefaultShellType = Settings.Default.DefaultShellType,
                Snippets = Settings.Default.Snippets,
                EncryptedServers = cloudEncryptedServers,
                AdditionalSettings = new Dictionary<string, string>
                {
                    // Add any additional settings here
                    ["LastSyncTimestamp"] = GetLocalSyncTimestamp().ToString("O")
                },
                LastSyncedAt = GetLocalSyncTimestamp(),
                DeviceId = DeviceId,
                DeviceName = DeviceName
            };
        }

        /// <summary>
        /// Applies cloud settings to local storage
        /// </summary>
        private void ApplyCloudSettingsLocally(UserSettingsData cloudSettings)
        {
            // Apply theme
            if (!string.IsNullOrEmpty(cloudSettings.CurrentTheme))
            {
                Settings.Default.CurrentTheme = cloudSettings.CurrentTheme;
            }

            // Apply shell type
            if (!string.IsNullOrEmpty(cloudSettings.DefaultShellType))
            {
                Settings.Default.DefaultShellType = cloudSettings.DefaultShellType;
            }

            // Apply servers - convert from cloud encryption to local encryption
            if (!string.IsNullOrEmpty(cloudSettings.EncryptedServers))
            {
                try
                {
                    // Decrypt using cloud keys (user-specific)
                    var decryptedServers = EncryptionHelper.Decrypt(cloudSettings.EncryptedServers);

                    // Re-encrypt using local keys (device-specific) for local storage
                    var localEncryptedServers = EncryptionHelper.Encrypt(decryptedServers);
                    Settings.Default.Servers = localEncryptedServers;

                    // Reload servers in ServerManager
                    var servers = JsonConvert.DeserializeObject<List<Models.Server>>(decryptedServers);
                    if (servers != null)
                    {
                        ServerManager.InitializeServers(servers);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading servers from cloud: {ex.Message}");
                }
            }
            // Apply shell type
            if (!string.IsNullOrEmpty(cloudSettings.Snippets))
            {
                // Decrypt using cloud keys (user-specific)
                var decryptedServers = EncryptionHelper.Decrypt(cloudSettings.Snippets);

                // Re-encrypt using local keys (device-specific) for local storage
                var localEncryptedServers = EncryptionHelper.Encrypt(decryptedServers);
                Settings.Default.Snippets = localEncryptedServers;
                var servers = JsonConvert.DeserializeObject<List<Models.Snippet>>(decryptedServers);
                if (servers != null)
                {
                    SnippetManager.InitializeSnippets(servers);
                }
            }

            // Update sync timestamp
            UpdateLocalSyncTimestamp(cloudSettings.LastSyncedAt);

            // Save all settings
            Settings.Default.Save();
        }

        /// <summary>
        /// Gets unique device identifier
        /// </summary>
        private static string GetDeviceId()
        {
            try
            {
                // Try to get hardware-based unique ID
                using (var mc = new ManagementClass("Win32_ComputerSystemProduct"))
                using (var moc = mc.GetInstances())
                {
                    foreach (ManagementObject mo in moc)
                    {
                        var uuid = mo.Properties["UUID"].Value?.ToString();
                        if (!string.IsNullOrEmpty(uuid) && uuid != "00000000-0000-0000-0000-000000000000")
                        {
                            return uuid;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to machine name + random GUID
            }

            // Fallback: Create persistent device ID based on machine name
            var machineNameBytes = Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(machineNameBytes);
                return Convert.ToBase64String(hash)[..16]; // Take first 16 characters
            }
        }

        /// <summary>
        /// Gets local sync timestamp
        /// </summary>
        private DateTime GetLocalSyncTimestamp()
        {
            try
            {
                string syncFile = GetSyncFile();
                if (File.Exists(syncFile))
                {
                    var timestampStr = File.ReadAllText(syncFile);
                    if (DateTime.TryParse(timestampStr, out var timestamp))
                    {
                        return timestamp.ToUniversalTime();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading sync timestamp: {ex.Message}");
            }
            return DateTime.MinValue;
        }

        private static string GetSyncFile()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScratchShell");
            var syncFile = Path.Combine(appDataPath, "lastsync.txt");
            return syncFile;
        }

        /// <summary>
        /// Updates local sync timestamp
        /// </summary>
        private void UpdateLocalSyncTimestamp(DateTime timestamp)
        {
            try
            {
                // Update both file-based and settings-based storage
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScratchShell");
                Directory.CreateDirectory(appDataPath);
                var syncFile = Path.Combine(appDataPath, "lastsync.txt");
                File.WriteAllText(syncFile, timestamp.ToString("O"));

                // Also update through UserSettingsService for UI binding
                UserSettingsService.UpdateLastSyncTimestamp(timestamp);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating sync timestamp: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears local sync timestamp
        /// </summary>
        private void ClearLocalSyncTimestamp()
        {
            try
            {
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScratchShell");
                var syncFile = Path.Combine(appDataPath, "lastsync.txt");
                if (File.Exists(syncFile))
                {
                    File.Delete(syncFile);
                }

                // Also clear the Settings-based timestamp
                Settings.Default.LastSyncTimestamp = string.Empty;
                Settings.Default.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing sync timestamp: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if there are local changes since last sync
        /// </summary>
        private bool HasLocalChanges(DateTime serverLastSync)
        {
            var localLastSync = GetLocalSyncTimestamp();

            // If local timestamp is DateTime.MinValue, it means we haven't synced or data was deleted
            // In this case, we should not consider it as having local changes
            if (localLastSync == DateTime.MinValue)
            {
                return false;
            }

            // Use a small tolerance (1 second) to handle timing precision issues
            var tolerance = TimeSpan.FromSeconds(1);
            return localLastSync > serverLastSync.Add(tolerance);
        }
    }
}