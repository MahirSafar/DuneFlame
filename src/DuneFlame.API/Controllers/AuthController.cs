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
public class AuthController(IAuthService authService, IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    private readonly IAuthService _authService = authService;
    private readonly IConfiguration _config = config;
    private readonly IWebHostEnvironment _env = env;

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

        string frontendLoginUrl = GetFrontendUrl("FrontendUrls:LoginUrl");

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
        // Build the callback URL with proper scheme handling
        // In cloud environments (Cloud Run, etc.), forwarded headers ensure correct scheme
        var scheme = Request.Scheme;

        // Fallback: if in production and scheme is http, force https
        // (ForwardedHeaders middleware should handle this, but this is a safety net)
        if (!_env.IsDevelopment() && scheme == "http")
        {
            scheme = "https";
        }

        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Auth", null, scheme);
        var properties = _authService.ConfigureExternalLoginsAsync(provider, redirectUrl!).Result;
        return Challenge(properties, provider);
    }

        [HttpGet("external-callback")]
        public async Task<IActionResult> ExternalLoginCallback()
        {
            var response = await _authService.ExternalLoginCallbackAsync();

            var frontendCallbackUrl = GetFrontendUrl("FrontendUrls:CallbackUrl");

            var redirectUrl = $"{frontendCallbackUrl}?" +
                $"userId={Uri.EscapeDataString(response.Id.ToString())}&" +
                $"accessToken={Uri.EscapeDataString(response.AccessToken)}&" +
                $"refreshToken={Uri.EscapeDataString(response.RefreshToken)}&" +
                $"email={Uri.EscapeDataString(response.Email)}&" +
                $"firstName={Uri.EscapeDataString(response.FirstName)}&" +
                $"lastName={Uri.EscapeDataString(response.LastName)}&" +
                $"roles={Uri.EscapeDataString(string.Join(",", response.Roles))}";

            return Redirect(redirectUrl);
        }

        /// <summary>
        /// Returns the appropriate frontend URL based on the current environment
        /// </summary>
        private string GetFrontendUrl(string configKey)
        {
            if (_env.IsDevelopment())
            {
                return _config[configKey] ?? "http://localhost:3000/auth/google-callback";
            }

            // For production, use production URLs
            var productionKey = configKey.Replace("FrontendUrls:", "FrontendUrls:Production");
            return _config[productionKey] ?? _config[configKey] ?? "https://duneflame.web.app/auth/google-callback";
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