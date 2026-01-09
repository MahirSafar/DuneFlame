using DuneFlame.Application.DTOs.Auth;

namespace DuneFlame.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshTokenAsync(TokenRequest request);
    Task LogoutAsync(string userId); // Refresh tokeni silmək üçün
}
