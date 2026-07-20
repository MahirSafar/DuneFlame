using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Users.Queries.GetUserProfile;

public class GetUserProfileQueryHandler(IUserProfileService userProfileService)
    : IRequestHandler<GetUserProfileQuery, UserProfileDto>
{
    public Task<UserProfileDto> Handle(GetUserProfileQuery query, CancellationToken cancellationToken)
        => userProfileService.GetOrCreateProfileAsync(query.UserId);
}
