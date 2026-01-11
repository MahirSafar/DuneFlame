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

        // Frontend URL-ni hardcode yazmaq əvəzinə appsettings.json-dan oxumaq daha yaxşıdır
        // Hələlik birbaşa yazıram:
        string frontendLoginUrl = "http://localhost:3000/auth/login";

        if (result)
        {
            // Uğurlu olduqda login səhifəsinə parametr göndərərək yönləndir
            return Redirect($"{frontendLoginUrl}?verified=true");
        }

        // Xəta olduqda xəta parametri ilə yönləndir
        return Redirect($"{frontendLoginUrl}?verified=false&error=invalid_token");
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
        var response = await _authService.ExternalLoginCallbackAsync();

        // Tokenləri URL vasitəsilə Next.js-ə ötürürük (və ya cookie ilə)
        var redirectUrl = $"http://localhost:3000/auth/callback?token={response.AccessToken}&refreshToken={response.RefreshToken}";
        return Redirect(redirectUrl);
    }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            var result = await _authService.ForgotPasswordAsync(request.Email);
            // Always return success for security (don't reveal if email exists)
            return Ok(new { message = "If an account with that email exists, a password reset link has been sent." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (request.NewPassword != request.ConfirmPassword)
                return BadRequest("Passwords do not match.");

            try
            {
                await _authService.ResetPasswordAsync(request.Email, request.Token, request.NewPassword);
                return Ok(new { message = "Password has been successfully reset. Please log in with your new password." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }