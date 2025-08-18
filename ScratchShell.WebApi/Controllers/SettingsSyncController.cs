using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScratchShell.WebApi.Data;
using ScratchShell.WebApi.DTOs;
using ScratchShell.WebApi.Models;
using System.Security.Claims;

namespace ScratchShell.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SettingsSyncController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<SettingsSyncController> _logger;

        public SettingsSyncController(
            ApplicationDbContext context,
            UserManager<User> userManager,
            ILogger<SettingsSyncController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<GetSettingsResponseDto>> GetSettings()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new GetSettingsResponseDto
                    {
                        IsSuccess = false,
                        Message = "Invalid token"
                    });
                }

                var userSettings = await _context.UserSettings
                    .FirstOrDefaultAsync(us => us.UserId == userId);

                if (userSettings == null)
                {
                    return Ok(new GetSettingsResponseDto
                    {
                        IsSuccess = true,
                        Message = "No settings found",
                        HasSettings = false
                    });
                }

                return Ok(new GetSettingsResponseDto
                {
                    IsSuccess = true,
                    Message = "Settings retrieved successfully",
                    HasSettings = true,
                    Settings = new UserSettingsDto
                    {
                        CurrentTheme = userSettings.CurrentTheme,
                        DefaultShellType = userSettings.DefaultShellType,
                        EncryptedServers = userSettings.EncryptedServers,
                        AdditionalSettings = userSettings.AdditionalSettings,
                        LastSyncedAt = userSettings.LastSyncedAt,
                        DeviceId = userSettings.DeviceId,
                        DeviceName = userSettings.DeviceName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving settings for user");
                return StatusCode(500, new GetSettingsResponseDto
                {
                    IsSuccess = false,
                    Message = "An error occurred while retrieving settings"
                });
            }
        }

        [HttpPost("sync")]
        public async Task<ActionResult<SyncSettingsResponseDto>> SyncSettings([FromBody] SyncSettingsRequestDto request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new SyncSettingsResponseDto
                    {
                        IsSuccess = false,
                        Message = "Invalid token"
                    });
                }

                var existingSettings = await _context.UserSettings
                    .FirstOrDefaultAsync(us => us.UserId == userId);

                var now = DateTime.UtcNow;

                if (existingSettings == null)
                {
                    // Create new settings
                    var newSettings = new UserSettings
                    {
                        UserId = userId,
                        CurrentTheme = request.Settings.CurrentTheme,
                        DefaultShellType = request.Settings.DefaultShellType,
                        EncryptedServers = request.Settings.EncryptedServers,
                        AdditionalSettings = request.Settings.AdditionalSettings,
                        DeviceId = request.Settings.DeviceId,
                        DeviceName = request.Settings.DeviceName,
                        LastSyncedAt = now.ToUniversalTime(),
                        UpdatedAt = now.ToUniversalTime()
                    };

                    _context.UserSettings.Add(newSettings);
                    await _context.SaveChangesAsync();

                    return Ok(new SyncSettingsResponseDto
                    {
                        IsSuccess = true,
                        Message = "Settings synced successfully",
                        Settings = request.Settings,
                        ServerLastSyncedAt = now,
                        ClientLastSyncedAt = request.Settings.LastSyncedAt
                    });
                }

                // Check for conflicts
                bool hasConflict = existingSettings.LastSyncedAt > request.Settings.LastSyncedAt && !request.ForceOverwrite;
                
                if (hasConflict)
                {
                    return Ok(new SyncSettingsResponseDto
                    {
                        IsSuccess = false,
                        Message = "Sync conflict detected. Server has newer settings.",
                        HasConflict = true,
                        Settings = new UserSettingsDto
                        {
                            CurrentTheme = existingSettings.CurrentTheme,
                            DefaultShellType = existingSettings.DefaultShellType,
                            EncryptedServers = existingSettings.EncryptedServers,
                            AdditionalSettings = existingSettings.AdditionalSettings,
                            LastSyncedAt = existingSettings.LastSyncedAt,
                            DeviceId = existingSettings.DeviceId,
                            DeviceName = existingSettings.DeviceName
                        },
                        ServerLastSyncedAt = existingSettings.LastSyncedAt,
                        ClientLastSyncedAt = request.Settings.LastSyncedAt
                    });
                }

                // Update existing settings
                existingSettings.CurrentTheme = request.Settings.CurrentTheme;
                existingSettings.DefaultShellType = request.Settings.DefaultShellType;
                existingSettings.EncryptedServers = request.Settings.EncryptedServers;
                existingSettings.AdditionalSettings = request.Settings.AdditionalSettings;
                existingSettings.DeviceId = request.Settings.DeviceId;
                existingSettings.DeviceName = request.Settings.DeviceName;
                existingSettings.LastSyncedAt = now;
                existingSettings.UpdatedAt = now;

                await _context.SaveChangesAsync();

                return Ok(new SyncSettingsResponseDto
                {
                    IsSuccess = true,
                    Message = "Settings synced successfully",
                    Settings = request.Settings,
                    ServerLastSyncedAt = now.ToUniversalTime(),
                    ClientLastSyncedAt = request.Settings.LastSyncedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing settings for user");
                return StatusCode(500, new SyncSettingsResponseDto
                {
                    IsSuccess = false,
                    Message = "An error occurred while syncing settings"
                });
            }
        }

        [HttpDelete]
        public async Task<ActionResult<DeleteSettingsResponseDto>> DeleteSettings()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new DeleteSettingsResponseDto
                    {
                        IsSuccess = false,
                        Message = "Invalid token"
                    });
                }

                var userSettings = await _context.UserSettings
                    .FirstOrDefaultAsync(us => us.UserId == userId);

                if (userSettings != null)
                {
                    _context.UserSettings.Remove(userSettings);
                    await _context.SaveChangesAsync();
                }

                return Ok(new DeleteSettingsResponseDto
                {
                    IsSuccess = true,
                    Message = "Settings deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting settings for user");
                return StatusCode(500, new DeleteSettingsResponseDto
                {
                    IsSuccess = false,
                    Message = "An error occurred while deleting settings"
                });
            }
        }

        [HttpPost("resolve-conflict")]
        public async Task<ActionResult<SyncSettingsResponseDto>> ResolveConflict([FromBody] SyncSettingsRequestDto request)
        {
            // Force overwrite to resolve conflicts
            request.ForceOverwrite = true;
            return await SyncSettings(request);
        }
    }
}