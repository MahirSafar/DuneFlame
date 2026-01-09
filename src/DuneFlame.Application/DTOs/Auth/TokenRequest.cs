namespace DuneFlame.Application.DTOs.Auth;

public record TokenRequest(
    string AccessToken,
    string RefreshToken
);
