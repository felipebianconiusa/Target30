using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly Target30DbContext _db;

    public SettingsController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await GetOrCreateSettingsAsync();
        return Ok(ToDto(settings));
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] SettingsDto request)
    {
        var settings = await GetOrCreateSettingsAsync();
        settings.GlobalTargetUtilizationPercent = Math.Clamp(request.GlobalTargetUtilizationPercent, 0, 100);
        settings.NotifyDaysBeforeClosing = Math.Clamp(request.NotifyDaysBeforeClosing, 0, 30);
        settings.NotificationsEnabled = request.NotificationsEnabled;
        settings.WeeklyDigestEnabled = request.WeeklyDigestEnabled;
        await _db.SaveChangesAsync();
        return Ok(ToDto(settings));
    }

    private static SettingsDto ToDto(UserSettings settings) => new(
        settings.GlobalTargetUtilizationPercent,
        settings.NotifyDaysBeforeClosing,
        settings.NotificationsEnabled,
        settings.WeeklyDigestEnabled,
        settings.Email);

    private async Task<UserSettings> GetOrCreateSettingsAsync()
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (settings is null)
        {
            settings = new UserSettings { UserId = CurrentUserId };
            _db.UserSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }
}

public record SettingsDto(
    decimal GlobalTargetUtilizationPercent,
    int NotifyDaysBeforeClosing,
    bool NotificationsEnabled,
    bool WeeklyDigestEnabled,
    string? Email);
