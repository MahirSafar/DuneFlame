using DuneFlame.Application.DTOs.User;
using MediatR;

namespace DuneFlame.Application.Users.Commands.UpdateProfile;

public record UpdateProfileCommand(Guid UserId, UpdateProfileRequest Request) : IRequest;
