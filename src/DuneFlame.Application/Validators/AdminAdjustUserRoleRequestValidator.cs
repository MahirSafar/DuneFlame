using DuneFlame.Application.DTOs.Admin;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class AdminAdjustUserRoleRequestValidator : AbstractValidator<AdminAdjustUserRoleRequest>
{
    public AdminAdjustUserRoleRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");

        RuleFor(x => x.Role)
            .NotEmpty()
            .WithMessage("Role is required.")
            .Must(r => r == "Admin" || r == "User")
            .WithMessage("Role must be either 'Admin' or 'User'.");
    }
}
