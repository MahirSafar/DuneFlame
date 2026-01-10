using DuneFlame.Application.Common;
using DuneFlame.Application.DTOs.User;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class UpdateProfileValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.Address)
            .MaximumLength(200)
            .MustBeSafeInput(); // XSS check

        RuleFor(x => x.City)
            .MaximumLength(100)
            .MustBeSafeInput();

        RuleFor(x => x.Country)
            .MaximumLength(100)
            .MustBeSafeInput();

        RuleFor(x => x.DateOfBirth)
            .LessThan(DateTime.UtcNow).WithMessage("Date of birth cannot be in the future.");

        RuleFor(x => x.AvatarUrl)
            .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrEmpty(x.AvatarUrl))
            .WithMessage("Invalid URL format for Avatar.");
    }
}