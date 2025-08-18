using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ScratchShell.WebApi.DTOs;
using ScratchShell.WebApi.Models;
using ScratchShell.WebApi.Services;

namespace ScratchShell.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IJwtService _jwtService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            IJwtService jwtService,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _jwtService = jwtService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new AuthResponseDto
                    {
                        IsSuccess = false,
                        Message = "Invalid input data"
                    });
                }

                var user = await _userManager.FindByEmailAsync(request.Email);
                if (user == null)
                {
                    return Unauthorized(new AuthResponseDto
                    {
                        IsSuccess = false,
                        Message = "Invalid email or password"
                    });
                }

                var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
                if (!result.Succeeded)
                {
                    return Unauthorized(new AuthResponseDto
                    {
                        IsSuccess = false,
                        Message = "Invalid email or password"
                    });
                }

                if (!user.IsActive)
                {
                    return Unauthorized(new AuthResponseDto
                    {
                        IsSuccess = false,
                        Message = "Account is deactivated"
                    });
                }

                var token = _jwtService.GenerateToken(user);
                var expiresAt = DateTime.UtcNow.AddMinutes(60); // Match with JWT expiry

                return Ok(new AuthResponseDto
                {
                    IsSuccess = true,
                    Message = "Login successful",
                    Token = token,
                    ExpiresAt = expiresAt,
                    User = new UserDto
                    {
                        Id = user.Id,
                        Email = user.Email ?? string.Empty,
                        UserName = user.UserName ?? string.Empty,
                        FirstName = user.FirstName,
                        LastName = user.LastName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for email: {Email}", request.Email);
                return StatusCode(500, new AuthResponseDto
                {
                    IsSuccess = false,
                    Message = "An error occurred during login"
                });
            }
        }

        [HttpPost("register")]
        public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterRequestDto request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new AuthResponseDto
                    {
                        IsSuccess = false,
                        Message = "Invalid input data"
                    });
                }

                // Check if user already exists
                var existingUser = await _userManager.FindByEmailAsync(request.Email);
                if (existingUser != null)
                {
                    return Conflict(new AuthResponseDto
                    {
                        IsSuccess = false,
                        Message = "User with this email already exists"
                    });
                }

                // Check if username is taken (if provided)
                if (!string.IsNullOrEmpty(request.UserName))
                {
                    var existingUsername = await _userManager.FindByNameAsync(request.UserName);
                    if (existingUsername != null)
                    {
                        return Conflict(new AuthResponseDto
                        {
                            IsSuccess = false,
                            Message = "Username is already taken"
                        });
                    }
                }

                // Create new user
                var user = new User
                {
                    Email = request.Email,
                    UserName = request.UserName ?? request.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                var result = await _userManager.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    return BadRequest(new AuthResponseDto
                    {
                        IsSuccess = false,
                        Message = $"Failed to create user: {errors}"
                    });
                }

                // Generate token for immediate login
                var token = _jwtService.GenerateToken(user);
                var expiresAt = DateTime.UtcNow.AddMinutes(60);

                _logger.LogInformation("User registered successfully: {Email}", request.Email);

                return CreatedAtAction(nameof(Register), new AuthResponseDto
                {
                    IsSuccess = true,
                    Message = "User registered successfully",
                    Token = token,
                    ExpiresAt = expiresAt,
                    User = new UserDto
                    {
                        Id = user.Id,
                        Email = user.Email,
                        UserName = user.UserName,
                        FirstName = user.FirstName,
                        LastName = user.LastName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for email: {Email}", request.Email);
                return StatusCode(500, new AuthResponseDto
                {
                    IsSuccess = false,
                    Message = "An error occurred during registration"
                });
            }
        }

        [HttpPost("logout")]
        public async Task<ActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return Ok(new { Message = "Logout successful" });
        }
    }
}