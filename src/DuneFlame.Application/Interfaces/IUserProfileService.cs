using DuneFlame.Application.DTOs.User;
using DuneFlame.Domain.Entities;

namespace DuneFlame.Application.Interfaces;

public interface IUserProfileService
{
    Task<UserProfile> GetOrCreateProfileAsync(Guid userId);
    Task UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
}
