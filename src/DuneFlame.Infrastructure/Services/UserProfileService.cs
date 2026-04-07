using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using DuneFlame.Infrastructure.Persistence; // AppDbContext üçün

namespace DuneFlame.Infrastructure.Services;

public class UserProfileService(AppDbContext context) : IUserProfileService
{
    private readonly AppDbContext _context = context;

    public async Task<UserProfile> GetOrCreateProfileEntityAsync(Guid userId)
    {
        var profile = await _context.UserProfiles
            .Include(u => u.User) // User məlumatlarını da gətir (Ad, Soyad, Email)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null)
        {
            profile = new UserProfile { UserId = userId };
            await _context.UserProfiles.AddAsync(profile);
            await _context.SaveChangesAsync();

            // User obyektini yenidən yüklə ki, null olmasın return edəndə
            await _context.Entry(profile).Reference(p => p.User).LoadAsync();
        }

        return profile;
    }

    public async Task<UserProfileDto> GetOrCreateProfileAsync(Guid userId)
    {
        var profile = await GetOrCreateProfileEntityAsync(userId);

        bool hasOrders = await _context.Orders
            .AnyAsync(o => o.UserId == userId && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Pending);

        return new UserProfileDto
        {
            UserId = profile.UserId,
            FirstName = profile.User?.FirstName,
            LastName = profile.User?.LastName,
            Email = profile.User?.Email,
            Address = profile.Address,
            City = profile.City,
            Country = profile.Country,
            AvatarUrl = profile.AvatarUrl,
            DateOfBirth = profile.DateOfBirth,
            HasOrders = hasOrders
        };
    }

    public async Task UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var profile = await GetOrCreateProfileEntityAsync(userId);

        // Partial Update: Yalnız dolu gələn sahələri yenilə
        if (!string.IsNullOrEmpty(request.Address)) profile.Address = request.Address;
        if (!string.IsNullOrEmpty(request.City)) profile.City = request.City;
        if (!string.IsNullOrEmpty(request.Country)) profile.Country = request.Country;
        if (!string.IsNullOrEmpty(request.AvatarUrl)) profile.AvatarUrl = request.AvatarUrl;
        if (request.DateOfBirth.HasValue) profile.DateOfBirth = request.DateOfBirth;

        profile.UpdatedAt = DateTime.UtcNow;
        _context.UserProfiles.Update(profile);
        await _context.SaveChangesAsync();
    }
}