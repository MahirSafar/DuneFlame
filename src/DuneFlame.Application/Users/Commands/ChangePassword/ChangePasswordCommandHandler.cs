using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Users.Commands.ChangePassword;

public class ChangePasswordCommandHandler(IUserProfileService userProfileService)
    : IRequestHandler<ChangePasswordCommand>
{
    public Task Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
        => userProfileService.ChangePasswordAsync(command.UserId, command.Request);
}
