using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/users")]
[ApiController]
[Authorize]
public class UserProfileController(IUserProfileService profileService) : ControllerBase
{
    private readonly IUserProfileService _profileService = profileService;

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var profile = await _profileService.GetOrCreateProfileAsync(userId);
        return Ok(profile);
    }

    [HttpPut("me")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateMyProfile([FromForm] UpdateProfileRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _profileService.UpdateProfileAsync(userId, request);
        return Ok(new { message = "Profile updated successfully" });
    }

    [HttpPut("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _profileService.ChangePasswordAsync(userId, request);
        return Ok(new { message = "Password updated successfully" });
    }
}
