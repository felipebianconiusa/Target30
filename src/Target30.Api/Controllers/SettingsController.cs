using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;
using Target30.Api.Models;
using Target30.Api.Services;

namespace Target30.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly Target30DbContext _db;
    private readonly IPushSender _push;

    public SettingsController(Target30DbContext db, IPushSender push)
    {
        _db = db;
        _push = push;
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
        var topic = request.PushTopic?.Trim();
        if (!string.IsNullOrEmpty(topic) && !NtfyPushSender.IsValidTopic(topic))
            return BadRequest(new { code = "invalid_push_topic" });

        var settings = await GetOrCreateSettingsAsync();
        settings.PushTopic = string.IsNullOrEmpty(topic) ? null : topic;
        settings.GlobalTargetUtilizationPercent = Math.Clamp(request.GlobalTargetUtilizationPercent, 0, 100);
        settings.NotifyDaysBeforeClosing = Math.Clamp(request.NotifyDaysBeforeClosing, 0, 30);
        settings.NotificationsEnabled = request.NotificationsEnabled;
        settings.WeeklyDigestEnabled = request.WeeklyDigestEnabled;
        settings.LowBalanceThreshold = Math.Max(request.LowBalanceThreshold, 0m);
        await _db.SaveChangesAsync();
        return Ok(ToDto(settings));
    }

    // Manda uma notificação de teste pro tópico salvo (pra conferir que o celular recebe).
    [HttpPost("push-test")]
    public async Task<IActionResult> PushTest()
    {
        var settings = await GetOrCreateSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.PushTopic))
            return BadRequest(new { code = "no_push_topic" });

        try
        {
            await _push.SendAsync(settings.PushTopic, "Target30", "Teste: as notificações no celular estão funcionando.");
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { code = "push_failed" });
        }

        return Ok();
    }

    private static SettingsDto ToDto(UserSettings settings) => new(
        settings.GlobalTargetUtilizationPercent,
        settings.NotifyDaysBeforeClosing,
        settings.NotificationsEnabled,
        settings.WeeklyDigestEnabled,
        settings.Email,
        settings.LastDigestSentDate,
        settings.LowBalanceThreshold,
        settings.PushTopic);

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
    string? Email,
    DateOnly? LastDigestSentDate,
    decimal LowBalanceThreshold = 0m,
    string? PushTopic = null);
