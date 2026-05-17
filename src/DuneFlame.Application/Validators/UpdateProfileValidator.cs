using DuneFlame.Application.Common;
using DuneFlame.Application.DTOs.User;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class UpdateProfileValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.FirstName)
            .MaximumLength(100)
            .MustBeSafeInput()
            .When(x => !string.IsNullOrEmpty(x.FirstName));

        RuleFor(x => x.LastName)
            .MaximumLength(100)
            .MustBeSafeInput()
            .When(x => !string.IsNullOrEmpty(x.LastName));

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30)
            .When(x => !string.IsNullOrEmpty(x.PhoneNumber));

        RuleFor(x => x.Address)
            .MaximumLength(200)
            .MustBeSafeInput()
            .When(x => !string.IsNullOrEmpty(x.Address));

        RuleFor(x => x.City)
            .MaximumLength(100)
            .MustBeSafeInput()
            .When(x => !string.IsNullOrEmpty(x.City));

        RuleFor(x => x.Country)
            .MaximumLength(100)
            .MustBeSafeInput()
            .When(x => !string.IsNullOrEmpty(x.Country));

        RuleFor(x => x.DateOfBirth)
            .LessThan(DateTime.UtcNow).WithMessage("Date of birth cannot be in the future.")
            .When(x => x.DateOfBirth.HasValue);

        RuleFor(x => x.Image)
            .Must(f => f!.Length <= 5 * 1024 * 1024).WithMessage("Image must not exceed 5 MB.")
            .Must(f => new[] { "image/jpeg", "image/png", "image/webp" }.Contains(f!.ContentType))
                .WithMessage("Only JPEG, PNG, and WebP images are allowed.")
            .When(x => x.Image != null);
    }
}
