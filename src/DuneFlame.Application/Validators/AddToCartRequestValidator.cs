using DuneFlame.Application.DTOs.Cart;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class AddToCartRequestValidator : AbstractValidator<AddToCartRequest>
{
    public AddToCartRequestValidator()
    {
        RuleFor(x => x.ProductVariantId)
            .NotEmpty()
            .WithMessage("ProductVariant ID is required.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .WithMessage("Quantity must be greater than 0.");
    }
}

