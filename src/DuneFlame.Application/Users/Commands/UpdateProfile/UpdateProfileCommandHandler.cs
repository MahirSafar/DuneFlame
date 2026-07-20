using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Users.Commands.UpdateProfile;

public class UpdateProfileCommandHandler(IUserProfileService userProfileService)
    : IRequestHandler<UpdateProfileCommand>
{
    public Task Handle(UpdateProfileCommand command, CancellationToken cancellationToken)
        => userProfileService.UpdateProfileAsync(command.UserId, command.Request);
}
