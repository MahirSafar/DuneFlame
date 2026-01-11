namespace DuneFlame.Application.DTOs.Admin;

public record AdminAdjustUserRoleRequest(
    Guid UserId,
    string Role
);
