namespace DuneFlame.Application.DTOs.Auth;

public record AuthResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string AccessToken,
    string RefreshToken,
    List<string> Roles
);
