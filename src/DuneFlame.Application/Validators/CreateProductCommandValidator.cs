using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Products.Commands.CreateProduct;
using FluentValidation;

namespace DuneFlame.Application.Validators;

/// <summary>Validator for CreateProductCommand — mirrors CreateProductRequestValidator rules.</summary>
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    private readonly ICategoryService _categoryService;

    public CreateProductCommandValidator(ICategoryService categoryService)
    {
        _categoryService = categoryService;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required.")
            .Length(3, 255).WithMessage("Product name must be between 3 and 255 characters.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Product description is required.")
            .MinimumLength(10).WithMessage("Product description must be at least 10 characters.");

        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("Category ID is required.")
            .MustAsync(async (id, ct) => await categoryService.IsLeafCategoryAsync(id))
            .WithMessage("Products can only be assigned to leaf categories.");

        WhenAsync(async (x, ct) => await IsCoffeeCategory(x.CategoryId, ct), () =>
        {
            RuleFor(x => x.OriginId).NotEmpty().WithMessage("Origin is required for coffee products.");
            RuleFor(x => x.RoastLevelIds).NotEmpty().WithMessage("At least one roast level is required for coffee.");
            RuleFor(x => x.GrindTypeIds).NotEmpty().WithMessage("At least one grind type is required for coffee.");
        }).Otherwise(() =>
        {
            RuleFor(x => x.OriginId).Null().WithMessage("Origin must be null for non-coffee products.");
            RuleFor(x => x.RoastLevelIds).Empty().WithMessage("Roast levels must be empty for non-coffee products.");
            RuleFor(x => x.GrindTypeIds).Empty().WithMessage("Grind types must be empty for non-coffee products.");
        });

        RuleFor(x => x.Variants).NotEmpty().WithMessage("At least one variant is required.");
    }

    private async Task<bool> IsCoffeeCategory(Guid categoryId, CancellationToken cancellationToken)
    {
        if (categoryId == Guid.Empty) return false;
        try
        {
            var category = await _categoryService.GetByIdAsync(categoryId);
            return category != null && category.IsCoffeeCategory;
        }
        catch
        {
            return false;
        }
    }
}
