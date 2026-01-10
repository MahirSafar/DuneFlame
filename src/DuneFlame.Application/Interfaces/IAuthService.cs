using DuneFlame.Application.DTOs.Auth;
using Microsoft.AspNetCore.Authentication;

namespace DuneFlame.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshTokenAsync(TokenRequest request);
    Task LogoutAsync(string userId); // Refresh tokeni silmək üçün
    Task<bool> VerifyEmailAsync(string userId, string token);
    Task<AuthenticationProperties> ConfigureExternalLoginsAsync(string provider, string redirectUrl);
    Task<AuthResponse> ExternalLoginCallbackAsync();
    Task<bool> ForgotPasswordAsync(string email);
    Task<bool> ResetPasswordAsync(string email, string token, string newPassword);
}
