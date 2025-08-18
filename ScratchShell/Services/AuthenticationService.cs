using System.Dynamic;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ScratchShell.Services
{
    public class AuthenticationService
    {
        public static string Token { get; set; } = string.Empty;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public AuthenticationService(HttpClient httpClient, string baseUrl = "https://localhost:7110")
        {
            _httpClient = httpClient;
            _baseUrl = baseUrl;
            
            // Try to load stored credentials on initialization
            LoadStoredCredentials();
            
            // Set default headers for all requests if token exists
            if (!string.IsNullOrEmpty(Token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
            }
        }

        /// <summary>
        /// Loads stored credentials from settings if available
        /// </summary>
        private void LoadStoredCredentials()
        {
            var storedCredentials = UserSettingsService.GetStoredCredentials();
            if (storedCredentials.HasValue)
            {
                Token = storedCredentials.Value.token ?? string.Empty;
                // Note: We cannot initialize user encryption keys here because we don't have the password
                // User will need to re-enter password for cloud sync functionality
            }
        }

        public async Task<LoginResult> LoginAsync(string username, string password)
        {
            try
            {
                var loginRequest = new LoginRequest
                {
                    Email = username,
                    Password = password
                };

                var json = JsonSerializer.Serialize(loginRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/auth/login", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var loginResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var token = loginResponse?.Token ?? string.Empty;
                    var userInfo = loginResponse?.User;

                    // Store credentials in settings
                    if (!string.IsNullOrEmpty(token) && userInfo != null)
                    {
                        var displayName = !string.IsNullOrEmpty(userInfo.UserName) ? userInfo.UserName : userInfo.Email;
                        
                        // Check if this is first time login
                        var isFirstTime = UserSettingsService.IsFirstTimeLogin();

                        // Initialize user-specific encryption keys for cloud sync
                        
                        // Store credentials (always store for persistence)
                        UserSettingsService.StoreAuthenticationCredentials(token, displayName, true);
                        SecureKeyStore.InitializeForUser(UserSettingsService.GetStoredUsername());


                        return new LoginResult
                        {
                            IsSuccess = true,
                            Token = token,
                            Message = "Login successful",
                            IsFirstTimeLogin = isFirstTime,
                            UserInfo = userInfo
                        };
                    }

                    return new LoginResult
                    {
                        IsSuccess = true,
                        Token = token,
                        Message = "Login successful"
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return new LoginResult
                    {
                        IsSuccess = false,
                        Message = $"Login failed: {response.StatusCode}"
                    };
                }
            }
            catch (HttpRequestException ex)
            {
                return new LoginResult
                {
                    IsSuccess = false,
                    Message = $"Network error: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                return new LoginResult
                {
                    IsSuccess = false,
                    Message = $"An error occurred: {ex.Message}"
                };
            }
        }

        public async Task<RegisterResult> RegisterAsync(string email, string password, string confirmPassword, string firstName, string lastName, string? userName = null)
        {
            try
            {
                var registerRequest = new RegisterRequest
                {
                    Email = email,
                    Password = password,
                    ConfirmPassword = confirmPassword,
                    FirstName = firstName,
                    LastName = lastName,
                    UserName = userName
                };

                var json = JsonSerializer.Serialize(registerRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/auth/register", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var registerResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var token = registerResponse?.Token ?? string.Empty;
                    var userInfo = registerResponse?.User;

                    // Store credentials in settings for new registration
                    if (!string.IsNullOrEmpty(token) && userInfo != null)
                    {
                        var displayName = !string.IsNullOrEmpty(userInfo.UserName) ? userInfo.UserName : userInfo.Email;

                        // Initialize user-specific encryption keys for cloud sync
                        
                        // Registration is always first time login
                        UserSettingsService.StoreAuthenticationCredentials(token, displayName, true);
                        SecureKeyStore.InitializeForUser(UserSettingsService.GetStoredUsername());


                        return new RegisterResult
                        {
                            IsSuccess = true,
                            Token = token,
                            Message = "Registration successful",
                            IsFirstTimeLogin = true,
                            UserInfo = userInfo
                        };
                    }

                    return new RegisterResult
                    {
                        IsSuccess = true,
                        Token = token,
                        Message = "Registration successful"
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    dynamic messageD = JsonSerializer.Deserialize<ExpandoObject>(errorContent);
                    return new RegisterResult
                    {
                        IsSuccess = false,
                        Message = $"Registration failed: {messageD.message}"
                    };
                }
            }
            catch (HttpRequestException ex)
            {
                return new RegisterResult
                {
                    IsSuccess = false,
                    Message = $"Network error: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                return new RegisterResult
                {
                    IsSuccess = false,
                    Message = $"An error occurred: {ex.Message}"
                };
            }
        }

        // Method to refresh token (even though it won't expire, useful for testing)
        public async Task<LoginResult> RefreshTokenAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(Token))
                {
                    return new LoginResult
                    {
                        IsSuccess = false,
                        Message = "No token available to refresh"
                    };
                }

                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/auth/refresh", null);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var refreshResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var newToken = refreshResponse?.Token ?? string.Empty;
                    
                    // Update stored token
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        var storedCredentials = UserSettingsService.GetStoredCredentials();
                        if (storedCredentials.HasValue)
                        {
                            UserSettingsService.StoreAuthenticationCredentials(
                                newToken, 
                                storedCredentials.Value.username ?? "", 
                                storedCredentials.Value.rememberMe);
                        }
                    }

                    return new LoginResult
                    {
                        IsSuccess = true,
                        Token = newToken,
                        Message = "Token refreshed successfully"
                    };
                }
                else
                {
                    return new LoginResult
                    {
                        IsSuccess = false,
                        Message = $"Token refresh failed: {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new LoginResult
                {
                    IsSuccess = false,
                    Message = $"Token refresh error: {ex.Message}"
                };
            }
        }

        // Method to validate if token is still valid (even though it won't expire)
        public static bool IsTokenValid()
        {
            return !string.IsNullOrEmpty(Token);
        }

        // Method to check if user has stored credentials
        public static bool HasStoredCredentials()
        {
            return UserSettingsService.GetStoredCredentials().HasValue;
        }

        // Method to get stored username
        public static string? GetStoredUsername()
        {
            return UserSettingsService.GetStoredUsername();
        }

        /// <summary>
        /// Re-initializes user encryption keys for cloud sync (needed when auto-logged in)
        /// </summary>
        /// <param name="password">User's password for key derivation</param>
        /// <returns>True if keys were successfully initialized</returns>
        public static bool InitializeEncryptionKeys(string password)
        {
            try
            {
                var username = GetStoredUsername();
                if (!string.IsNullOrEmpty(username))
                {
                    SecureKeyStore.InitializeForUser(username);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing encryption keys: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if cloud encryption keys are available
        /// </summary>
        public static bool HasCloudEncryptionKeys => SecureKeyStore.HasUserKeys;

        // Method to clear token (logout)
        public static void ClearToken()
        {
            Token = string.Empty;
            UserSettingsService.ClearStoredCredentials();
        }

        // Method to logout and clear all stored data
        public static void Logout()
        {
            // Clear user-specific encryption keys
            SecureKeyStore.ClearUserKeys();
            ClearToken();
        }
    }

    // Login DTOs
    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class AuthResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public UserInfo? User { get; set; }
    }

    public class LoginResult
    {
        public bool IsSuccess { get; set; }
        public string Token { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsFirstTimeLogin { get; set; } = false;
        public UserInfo? UserInfo { get; set; }
    }

    // Register DTOs
    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? UserName { get; set; }
    }

    public class RegisterResult
    {
        public bool IsSuccess { get; set; }
        public string Token { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsFirstTimeLogin { get; set; } = true;
        public UserInfo? UserInfo { get; set; }
    }

    public class UserInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }
}