namespace DuneFlame.Application.DTOs.Admin;

public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    List<string> Roles,
    bool IsLockedOut
);
