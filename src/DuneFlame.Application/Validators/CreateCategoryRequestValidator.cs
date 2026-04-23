using DuneFlame.Application.Interfaces;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator(ICategoryService categoryService)
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

        // Validate ParentCategoryId references a real row when provided and is not Guid.Empty
        RuleFor(x => x.ParentCategoryId)
            .MustAsync(async (parentId, cancellationToken) =>
            {
                if (!parentId.HasValue || parentId.Value == Guid.Empty) return true;
                try { await categoryService.GetByIdAsync(parentId.Value); return true; }
                catch { return false; }
            })
            .WithMessage("The specified parent category does not exist.");
    }
}
