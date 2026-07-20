using DuneFlame.Application.DTOs.User;
using MediatR;

namespace DuneFlame.Application.Users.Queries.GetUserProfile;

public record GetUserProfileQuery(Guid UserId) : IRequest<UserProfileDto>;
