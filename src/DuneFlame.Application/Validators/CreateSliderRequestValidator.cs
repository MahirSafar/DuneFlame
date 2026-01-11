using DuneFlame.Application.DTOs.Admin;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CreateSliderRequestValidator : AbstractValidator<CreateSliderRequest>
{
    public CreateSliderRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Title is required.")
            .MaximumLength(200)
            .WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.Subtitle)
            .MaximumLength(500)
            .WithMessage("Subtitle must not exceed 500 characters.");

        RuleFor(x => x.ImageUrl)
            .NotEmpty()
            .WithMessage("Image URL is required.")
            .Matches(@"^https?://")
            .WithMessage("Image URL must be a valid HTTP(S) URL.");

        RuleFor(x => x.TargetUrl)
            .MaximumLength(500)
            .WithMessage("Target URL must not exceed 500 characters.");

        RuleFor(x => x.Order)
            .GreaterThan(0)
            .WithMessage("Order must be greater than 0.");
    }
}
