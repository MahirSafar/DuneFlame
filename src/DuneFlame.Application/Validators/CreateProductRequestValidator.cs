using DuneFlame.Application.DTOs.Product;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
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

        RuleFor(x => x.Price)
            .GreaterThan(0)
            .WithMessage("Product price must be greater than 0.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Product price precision is invalid.");

        RuleFor(x => x.OldPrice)
            .GreaterThan(0)
            .When(x => x.OldPrice.HasValue)
            .WithMessage("Old price must be greater than 0 if provided.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Old price precision is invalid.");

        RuleFor(x => x.StockQuantity)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Stock quantity cannot be negative.");

        RuleFor(x => x.CategoryId)
            .NotEmpty()
            .WithMessage("Category ID is required.");

        RuleFor(x => x.Images)
            .Custom((images, context) =>
            {
                if (images == null || images.Count == 0)
                    return; // Images are optional

                if (images.Count > 10)
                    context.AddFailure("Images", "Maximum 10 images are allowed per product.");

                foreach (var image in images)
                {
                    if (image == null)
                        context.AddFailure("Images", "Image cannot be null.");

                    if (image!.Length == 0)
                        context.AddFailure("Images", "Image file is empty.");

                    if (image.Length > 2 * 1024 * 1024)
                        context.AddFailure("Images", "Image size cannot exceed 2MB.");

                    var allowedMimes = new[] { "image/jpeg", "image/png", "image/webp" };
                    if (!allowedMimes.Contains(image.ContentType?.ToLower() ?? ""))
                        context.AddFailure("Images", "Invalid image type. Only JPG, PNG and WEBP are allowed.");
                }
            });
    }
}
