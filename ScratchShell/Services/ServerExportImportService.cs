using Ionic.Zip;
using Newtonsoft.Json;
using ScratchShell.Models;
using System.IO;

namespace ScratchShell.Services
{
    internal static class ServerExportImportService
    {
        private const string TEMP_JSON_NAME = "servers.json";

        /// <summary>
        /// Exports selected servers to a password-protected .ss file
        /// </summary>
        /// <param name="servers">List of servers to export</param>
        /// <param name="filePath">Path where to save the .ss file</param>
        /// <param name="password">Optional password for encryption</param>
        public static async Task<bool> ExportServers(List<Server> servers, string filePath, string password = null)
        {
            try
            {
                // Ensure the file has .ss extension
                if (!filePath.EndsWith(".ss", StringComparison.OrdinalIgnoreCase))
                {
                    filePath += ".ss";
                }

                // Serialize servers to JSON
                var json = JsonConvert.SerializeObject(servers, Formatting.Indented);

                // Create a temporary directory for the JSON file
                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                try
                {
                    var tempJsonPath = Path.Combine(tempDir, TEMP_JSON_NAME);
                    await File.WriteAllTextAsync(tempJsonPath, json);

                    // Create ZIP file
                    using (var zip = new ZipFile())
                    {
                        // Set password if provided
                        if (!string.IsNullOrWhiteSpace(password))
                        {
                            zip.Password = password;
                            zip.Encryption = EncryptionAlgorithm.WinZipAes256;
                        }

                        // Add the JSON file to the ZIP
                        zip.AddFile(tempJsonPath, "");

                        // Save the ZIP file
                        zip.Save(filePath);
                    }

                    return true;
                }
                finally
                {
                    // Clean up temporary directory
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Export failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Imports servers from a .ss file
        /// </summary>
        /// <param name="filePath">Path to the .ss file</param>
        /// <param name="password">Optional password for decryption</param>
        /// <returns>List of imported servers or null if failed</returns>
        public static async Task<List<Server>> ImportServers(string filePath, string password = null)
        {
            try
            {
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException("Import file not found", filePath);
                }

                // Create a temporary directory for extraction
                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Extract ZIP file
                    using (var zip = ZipFile.Read(filePath))
                    {
                        // Set password if provided
                        if (!string.IsNullOrWhiteSpace(password))
                        {
                            zip.Password = password;
                        }

                        // Extract to temporary directory
                        zip.ExtractAll(tempDir, ExtractExistingFileAction.OverwriteSilently);
                    }

                    // Read the JSON file
                    var jsonPath = Path.Combine(tempDir, TEMP_JSON_NAME);
                    if (!File.Exists(jsonPath))
                    {
                        throw new InvalidOperationException("Invalid import file format");
                    }

                    var json = await File.ReadAllTextAsync(jsonPath);

                    // Deserialize servers
                    var servers = JsonConvert.DeserializeObject<List<Server>>(json);

                    // Generate new IDs to avoid conflicts
                    if (servers != null)
                    {
                        foreach (var server in servers)
                        {
                            server.Id = Guid.NewGuid().ToString();
                        }
                    }

                    return servers;
                }
                finally
                {
                    // Clean up temporary directory
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
            catch (BadPasswordException)
            {
                // Password incorrect
                throw new InvalidOperationException("Incorrect password");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Import failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Validates if a file is a valid .ss export file
        /// </summary>
        /// <param name="filePath">Path to the file to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool IsValidExportFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath) || !filePath.EndsWith(".ss", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Try to read the ZIP file structure
                using (var zip = ZipFile.Read(filePath))
                {
                    // Check if it contains the expected JSON file
                    return zip.EntryFileNames.Contains(TEMP_JSON_NAME);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}