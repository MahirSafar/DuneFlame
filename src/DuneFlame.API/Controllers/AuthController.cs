using DuneFlame.Application.DTOs.Auth;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/auth")]
[ApiController]
[EnableRateLimiting("AuthPolicy")]
public class AuthController(IAuthService authService) : ControllerBase
{
    private readonly IAuthService _authService = authService;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] TokenRequest request)
    {
        var result = await _authService.RefreshTokenAsync(request);
        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize] // Yalnız giriş etmiş istifadəçilər çıxış edə bilər
    public async Task<IActionResult> Logout()
    {
        // Token-dən User ID-ni oxuyuruq
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId == null) return Unauthorized();

        await _authService.LogoutAsync(userId);
        return Ok(new { message = "Logged out successfully" });
    }
    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string userId, [FromQuery] string token)
    {
        var result = await _authService.VerifyEmailAsync(userId, token);
        if (result) return Ok("Email successfully verified!");
        return BadRequest("Invalid verification link or token expired.");
    }
    [HttpGet("external-login")]
    public IActionResult ExternalLogin(string provider = "Google")
    {
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback));
        var properties = _authService.ConfigureExternalLoginsAsync(provider, redirectUrl!).Result;
        return Challenge(properties, provider);
    }

    [HttpGet("external-callback")]
    public async Task<IActionResult> ExternalLoginCallback()
    {
        try
        {
            var response = await _authService.ExternalLoginCallbackAsync();
            // Sonda front-end linkinə tokenlərlə yönləndirəcəyik, hələlik Ok qaytaraq
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}