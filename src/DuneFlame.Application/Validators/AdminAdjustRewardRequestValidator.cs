using DuneFlame.Application.DTOs.Reward;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class AdminAdjustRewardRequestValidator : AbstractValidator<AdminAdjustRewardRequest>
{
    public AdminAdjustRewardRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");

        RuleFor(x => x.Amount)
            .NotEqual(0)
            .WithMessage("Amount cannot be zero.");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Reason is required.")
            .MinimumLength(5)
            .WithMessage("Reason must be at least 5 characters long.")
            .MaximumLength(500)
            .WithMessage("Reason must not exceed 500 characters.");
    }
}
