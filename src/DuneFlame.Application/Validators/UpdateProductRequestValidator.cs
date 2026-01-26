using DuneFlame.Application.DTOs.Product;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
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

        RuleFor(x => x.StockInKg)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Stock in kg must be greater than or equal to 0.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Stock precision is invalid.");

        RuleFor(x => x.CategoryId)
            .NotEmpty()
            .WithMessage("Category ID is required.");

        RuleFor(x => x.RoastLevelIds)
            .NotEmpty()
            .WithMessage("At least one roast level must be selected.");

        RuleFor(x => x.GrindTypeIds)
            .NotEmpty()
            .WithMessage("At least one grind type must be selected.");

        RuleFor(x => x.Prices)
            .NotEmpty()
            .WithMessage("At least one price entry is required.")
            .ForEach(price =>
            {
                price.ChildRules(p =>
                {
                    p.RuleFor(pr => pr.Price)
                        .GreaterThan(0)
                        .WithMessage("Each price must be greater than 0.");
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
}
