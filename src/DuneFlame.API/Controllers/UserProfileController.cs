using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Users.Commands.ChangePassword;
using DuneFlame.Application.Users.Commands.UpdateProfile;
using DuneFlame.Application.Users.Queries.GetUserProfile;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/users")]
[ApiController]
[Authorize]
public class UserProfileController(IMediator mediator) : ControllerBase
{
    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var profile = await mediator.Send(new GetUserProfileQuery(GetUserId()));
        return Ok(profile);
    }

    [HttpPut("me")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateMyProfile([FromForm] UpdateProfileRequest request)
    {
        await mediator.Send(new UpdateProfileCommand(GetUserId(), request));
        return Ok(new { message = "Profile updated successfully" });
    }

    [HttpPut("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        await mediator.Send(new ChangePasswordCommand(GetUserId(), request));
        return Ok(new { message = "Password updated successfully" });
    }
}
