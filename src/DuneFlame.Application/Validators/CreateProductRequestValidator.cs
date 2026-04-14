using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    private readonly ICategoryService _categoryService;

    public CreateProductRequestValidator(ICategoryService categoryService)
    {
        _categoryService = categoryService;

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Product name is required.")
            .Length(3, 255)
            .WithMessage("Product name must be between 3 and 255 characters.");

        RuleFor(x => x.Description)
            .NotEmpty()
            .WithMessage("Product description is required.")
            .MinimumLength(10)
            .WithMessage("Product description must be at least 10 characters.");

        RuleFor(x => x.CategoryId)
            .NotEmpty()
            .WithMessage("Category ID is required.");

        WhenAsync(async (x, cancellation) => await IsCoffeeCategory(x.CategoryId, cancellation), () =>
        {
            RuleFor(x => x.OriginId)
                .NotEmpty()
                .WithMessage("Origin is required for coffee products.");

            RuleFor(x => x.RoastLevelIds)
                .NotEmpty()
                .WithMessage("At least one roast level must be selected for coffee products.");

            RuleFor(x => x.GrindTypeIds)
                .NotEmpty()
                .WithMessage("At least one grind type must be selected for coffee products.");
        }).Otherwise(() =>
        {
            RuleFor(x => x.OriginId)
                .Null()
                .WithMessage("Origin must be null for non-coffee products.");

            RuleFor(x => x.RoastLevelIds)
                .Empty()
                .WithMessage("Roast levels must be empty for non-coffee products.");

            RuleFor(x => x.GrindTypeIds)
                .Empty()
                .WithMessage("Grind types must be empty for non-coffee products.");
        });

        RuleFor(x => x.Variants)
            .NotEmpty()
            .WithMessage("At least one variant entry is required.")
            .Must(variants => variants != null && variants.Select(v => v.Sku).Distinct().Count() == variants.Count)
            .WithMessage("Variant SKUs must be unique within the creation request.")
            .ForEach(variant =>
            {
                variant.ChildRules(p =>
                {
                    p.RuleFor(pr => pr.Prices)
                        .NotEmpty()
                        .WithMessage("At least one price entry is required.");

                    p.RuleForEach(pr => pr.Prices).ChildRules(price => {
                        price.RuleFor(pr => pr.Price)
                             .GreaterThan(0)
                             .WithMessage("Each price must be greater than 0.");
                    });
                    p.RuleFor(pr => pr.StockQuantity)
                        .GreaterThanOrEqualTo(0)
                        .WithMessage("Stock quantity must be greater than or equal to 0.");
                });
            });

        RuleFor(x => x.Images)
            .Custom((images, context) =>
            {
                if (images == null || images.Count == 0)
                    return;

                if (images.Count > 10)
                    context.AddFailure("Images", "Maximum 10 images are allowed per product.");

                foreach (var image in images)
                {
                    if (!IsValidImageFile(image))
                        context.AddFailure("Images", "Only image files (JPEG, PNG, GIF) are allowed.");
                }
            });
    }

    private static bool IsValidImageFile(Microsoft.AspNetCore.Http.IFormFile file)
    {
        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif" };
        return allowedMimeTypes.Contains(file.ContentType?.ToLower());
    }

    private async Task<bool> IsCoffeeCategory(Guid categoryId, CancellationToken cancellationToken)
    {
        try
        {
            var category = await _categoryService.GetByIdAsync(categoryId);
            return category != null && category.IsCoffeeCategory;
        }
        catch
        {
            return false; // If category not found, default to not coffee
        }
    }
}

