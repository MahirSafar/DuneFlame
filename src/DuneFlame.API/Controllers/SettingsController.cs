using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/settings")]
[ApiController]
[Authorize(Roles = "Admin")]
public class SettingsController(ISettingsService settingsService) : ControllerBase
{
    private readonly ISettingsService _settingsService = settingsService;

    [HttpGet("{key}")]
    public async Task<IActionResult> GetSetting(string key)
    {
        try
        {
            var value = await _settingsService.GetSettingAsync(key);
            if (value == null)
            {
                return NotFound(new { message = $"Setting '{key}' not found" });
            }
            return Ok(new { key, value });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SetSetting([FromBody] SetSettingRequest request)
    {
        try
        {
            await _settingsService.SetSettingAsync(request.Key, request.Value);
            return Ok(new { message = "Setting updated successfully", key = request.Key, value = request.Value });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public record SetSettingRequest(string Key, string Value);
