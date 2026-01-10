using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using DuneFlame.Infrastructure.Persistence; // AppDbContext üçün

namespace DuneFlame.Infrastructure.Services;

public class UserProfileService(AppDbContext context) : IUserProfileService
{
    private readonly AppDbContext _context = context;

    public async Task<UserProfile> GetOrCreateProfileAsync(Guid userId)
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

    public async Task UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var profile = await GetOrCreateProfileAsync(userId);

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