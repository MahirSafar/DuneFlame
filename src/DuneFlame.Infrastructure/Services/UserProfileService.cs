using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DuneFlame.Infrastructure.Persistence;

namespace DuneFlame.Infrastructure.Services;

public class UserProfileService(
    AppDbContext context,
    UserManager<ApplicationUser> userManager,
    IFileService fileService) : IUserProfileService
{
    private readonly AppDbContext _context = context;
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IFileService _fileService = fileService;

    public async Task<UserProfile> GetOrCreateProfileEntityAsync(Guid userId)
    {
        var profile = await _context.UserProfiles
            .Include(u => u.User)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            profile = new UserProfile { UserId = userId };
            await _context.UserProfiles.AddAsync(profile);
            await _context.SaveChangesAsync();
            await _context.Entry(profile).Reference(p => p.User).LoadAsync();
        }

        return profile;
    }

    public async Task<UserProfileDto> GetOrCreateProfileAsync(Guid userId)
    {
        var profile = await GetOrCreateProfileEntityAsync(userId);

        bool hasOrders = await _context.Orders
            .AnyAsync(o => o.UserId == userId && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Pending);

        var avatarUrl = profile.AvatarUrl ?? profile.User?.ProfileImageUrl;

        return new UserProfileDto
        {
            UserId = profile.UserId,
            FirstName = profile.User?.FirstName,
            LastName = profile.User?.LastName,
            Email = profile.User?.Email,
            PhoneNumber = profile.User?.PhoneNumber,
            Address = profile.Address,
            City = profile.City,
            Country = profile.Country,
            AvatarUrl = avatarUrl,
            ProfileImageUrl = avatarUrl,
            DateOfBirth = profile.DateOfBirth,
            HasOrders = hasOrders,
            HasPassword = !string.IsNullOrEmpty(profile.User?.PasswordHash)
        };
    }

    public async Task UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var profile = await GetOrCreateProfileEntityAsync(userId);
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User not found.");

        if (!string.IsNullOrWhiteSpace(request.FirstName)) user.FirstName = request.FirstName;
        if (!string.IsNullOrWhiteSpace(request.LastName)) user.LastName = request.LastName;
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber)) user.PhoneNumber = request.PhoneNumber;
        await _userManager.UpdateAsync(user);

        if (!string.IsNullOrEmpty(request.Address)) profile.Address = request.Address;
        if (!string.IsNullOrEmpty(request.City)) profile.City = request.City;
        if (!string.IsNullOrEmpty(request.Country)) profile.Country = request.Country;
        if (request.DateOfBirth.HasValue) profile.DateOfBirth = request.DateOfBirth;

        if (request.Image != null)
        {
            var imageUrl = await _fileService.UploadImageAsync(request.Image, "avatars");
            profile.AvatarUrl = imageUrl;
        }

        profile.UpdatedAt = DateTime.UtcNow;
        _context.UserProfiles.Update(profile);
        await _context.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            throw new BadRequestException("New password is required.");

        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User not found.");

        IdentityResult result;

        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            // Google/OAuth user — no existing password, set directly
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
        }
        else
        {
            // Regular user — verify old password first
            if (string.IsNullOrWhiteSpace(request.OldPassword))
                throw new BadRequestException("Old password is required.");

            result = await _userManager.ChangePasswordAsync(user, request.OldPassword, request.NewPassword);
        }

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException($"Password change failed: {errors}");
        }
    }
}
