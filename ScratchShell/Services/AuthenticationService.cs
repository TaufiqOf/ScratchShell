using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ScratchShell.Services
{
    public class AuthenticationService
    {
        public static string Token { get; set; }
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public AuthenticationService(HttpClient httpClient, string baseUrl = "https://localhost:7000")
        {
            _httpClient = httpClient;
            _baseUrl = baseUrl;
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
                    var loginResponse = JsonSerializer.Deserialize<LoginResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new LoginResult
                    {
                        IsSuccess = true,
                        Token = loginResponse?.Token,
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
                    var registerResponse = JsonSerializer.Deserialize<RegisterResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new RegisterResult
                    {
                        IsSuccess = true,
                        Token = registerResponse?.Token,
                        Message = "Registration successful"
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return new RegisterResult
                    {
                        IsSuccess = false,
                        Message = $"Registration failed: {response.StatusCode}"
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
    }

    // Login DTOs
    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public class LoginResult
    {
        public bool IsSuccess { get; set; }
        public string Token { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
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

    public class RegisterResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public UserInfo? User { get; set; }
    }

    public class RegisterResult
    {
        public bool IsSuccess { get; set; }
        public string Token { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
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