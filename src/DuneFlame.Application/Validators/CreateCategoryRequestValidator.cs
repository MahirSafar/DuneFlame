using DuneFlame.Application.Interfaces;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Category name is required.")
            .Length(3, 100)
            .WithMessage("Category name must be between 3 and 100 characters.");

        RuleFor(x => x.Slug)
            .NotEmpty()
            .WithMessage("Category slug is required.")
            .Matches(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("Slug must contain only lowercase letters, numbers and hyphens.");
    }
}
