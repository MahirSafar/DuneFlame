using DuneFlame.Application.DTOs.User;
using MediatR;

namespace DuneFlame.Application.Users.Commands.ChangePassword;

public record ChangePasswordCommand(Guid UserId, ChangePasswordRequest Request) : IRequest;
