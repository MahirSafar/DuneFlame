using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/users")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminUserController(
    IAdminUserService adminUserService,
    IValidator<AdminAdjustUserRoleRequest> roleValidator) : ControllerBase
{
    private readonly IAdminUserService _adminUserService = adminUserService;
    private readonly IValidator<AdminAdjustUserRoleRequest> _roleValidator = roleValidator;

    [HttpGet]
    public async Task<IActionResult> GetAllUsers()
    {
        try
        {
            var users = await _adminUserService.GetAllUsersAsync();
            return Ok(users);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/toggle-ban")]
    public async Task<IActionResult> ToggleUserBan(Guid id)
    {
        try
        {
            await _adminUserService.ToggleUserBanAsync(id);
            return Ok(new { message = "User ban status toggled successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/role")]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AdminAdjustUserRoleRequest request)
    {
        var validationResult = await _roleValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        try
        {
            // Validate that the request ID matches the route ID
            if (request.UserId != id)
            {
                return BadRequest(new { message = "User ID in request does not match URL parameter" });
            }

            await _adminUserService.AssignRoleAsync(id, request.Role);
            return Ok(new { message = $"User assigned to {request.Role} role successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
