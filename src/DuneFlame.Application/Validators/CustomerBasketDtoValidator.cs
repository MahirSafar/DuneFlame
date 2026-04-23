using DuneFlame.Application.DTOs.Basket;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CustomerBasketDtoValidator : AbstractValidator<CustomerBasketDto>
{
    public CustomerBasketDtoValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Basket ID is required.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.ProductVariantId)
                .NotEmpty()
                .WithMessage("Each basket item must have a valid ProductVariantId. Empty GUIDs are not allowed.");

            item.RuleFor(x => x.Quantity)
                .GreaterThan(0)
                .WithMessage("Each basket item must have a quantity greater than 0.");
        });
    }
}
