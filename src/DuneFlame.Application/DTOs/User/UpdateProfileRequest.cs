namespace DuneFlame.Application.DTOs.User;

public record UpdateProfileRequest(
    string? Address,
    string? City,
    string? Country,
    DateTime? DateOfBirth,
    string? AvatarUrl
);