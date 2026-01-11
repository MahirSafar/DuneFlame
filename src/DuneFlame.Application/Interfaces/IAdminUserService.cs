using DuneFlame.Application.DTOs.Admin;

namespace DuneFlame.Application.Interfaces;

public interface IAdminUserService
{
    Task<List<UserDto>> GetAllUsersAsync();
    Task ToggleUserBanAsync(Guid userId);
    Task AssignRoleAsync(Guid userId, string role);
}
