using DuneFlame.Application.DTOs.User;
using DuneFlame.Domain.Entities;

namespace DuneFlame.Application.Interfaces;

public interface IUserProfileService
{
    Task<UserProfileDto> GetOrCreateProfileAsync(Guid userId);
    Task UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
}
