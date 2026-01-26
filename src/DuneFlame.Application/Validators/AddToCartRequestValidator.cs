using DuneFlame.Application.DTOs.Cart;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class AddToCartRequestValidator : AbstractValidator<AddToCartRequest>
{
    public AddToCartRequestValidator()
    {
        RuleFor(x => x.ProductPriceId)
            .NotEmpty()
            .WithMessage("ProductPrice ID is required.");

        RuleFor(x => x.RoastLevelId)
            .NotEmpty()
            .WithMessage("Roast Level ID is required.");

        RuleFor(x => x.GrindTypeId)
            .NotEmpty()
            .WithMessage("Grind Type ID is required.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .WithMessage("Quantity must be greater than 0.");
    }
}

