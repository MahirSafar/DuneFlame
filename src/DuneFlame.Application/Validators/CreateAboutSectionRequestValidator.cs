using DuneFlame.Application.DTOs.Admin;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CreateAboutSectionRequestValidator : AbstractValidator<CreateAboutSectionRequest>
{
    public CreateAboutSectionRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Title is required.")
            .MaximumLength(200)
            .WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.Content)
            .NotEmpty()
            .WithMessage("Content is required.")
            .MaximumLength(5000)
            .WithMessage("Content must not exceed 5000 characters.");

        RuleFor(x => x.ImageUrl)
            .NotEmpty()
            .WithMessage("Image URL is required.")
            .Matches(@"^https?://")
            .WithMessage("Image URL must be a valid HTTP(S) URL.");
    }
}
