using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

public class AdminUserService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    ILogger<AdminUserService> logger) : IAdminUserService
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager = roleManager;
    private readonly ILogger<AdminUserService> _logger = logger;

    public async Task<List<UserDto>> GetAllUsersAsync()
    {
        try
        {
            var users = await _userManager.Users.ToListAsync();
            var userDtos = new List<UserDto>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var userDto = new UserDto(
                    user.Id,
                    user.Email ?? string.Empty,
                    $"{user.FirstName} {user.LastName}",
                    roles.ToList(),
                    user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow
                );
                userDtos.Add(userDto);
            }

            _logger.LogInformation("Retrieved {UserCount} users", userDtos.Count);
            return userDtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all users");
            throw;
        }
    }

    public async Task ToggleUserBanAsync(Guid userId)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                throw new NotFoundException($"User not found: {userId}");
            }

            if (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            {
                // User is banned, unban them
                await _userManager.SetLockoutEndDateAsync(user, null);
                _logger.LogInformation("User unbanned: {UserId}", userId);
            }
            else
            {
                // Ban user for 1 year
                var lockoutEnd = DateTime.UtcNow.AddYears(1);
                await _userManager.SetLockoutEndDateAsync(user, lockoutEnd);
                _logger.LogInformation("User banned until {LockoutEnd}: {UserId}", lockoutEnd, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling user ban: {UserId}", userId);
            throw;
        }
    }

    public async Task AssignRoleAsync(Guid userId, string role)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                throw new NotFoundException($"User not found: {userId}");
            }

            // Verify role exists
            var roleExists = await _roleManager.RoleExistsAsync(role);
            if (!roleExists)
            {
                throw new BadRequestException($"Role does not exist: {role}");
            }

            // Remove all roles and assign the new one
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Count > 0)
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            }

            await _userManager.AddToRoleAsync(user, role);
            _logger.LogInformation("User {UserId} assigned to role {Role}", userId, role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning role {Role} to user {UserId}", role, userId);
            throw;
        }
    }
}
