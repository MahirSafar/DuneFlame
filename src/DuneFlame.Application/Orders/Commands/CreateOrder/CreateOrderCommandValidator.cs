using FluentValidation;

namespace DuneFlame.Application.Orders.Commands.CreateOrder;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.BasketId)
            .NotEmpty().WithMessage("Basket ID is required.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Must(c => string.Equals(c, "USD", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(c, "AED", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Currency must be 'USD' or 'AED'.");

        RuleFor(x => x.ShippingAddress)
            .NotNull().WithMessage("Shipping address is required.");

        When(x => x.ShippingAddress != null, () =>
        {
            RuleFor(x => x.ShippingAddress.Street)
                .NotEmpty().WithMessage("Street address is required.")
                .MaximumLength(200);

            RuleFor(x => x.ShippingAddress.City)
                .NotEmpty().WithMessage("City is required.")
                .MinimumLength(2).MaximumLength(50);

            RuleFor(x => x.ShippingAddress.Country)
                .NotEmpty().WithMessage("Country is required.")
                .Must(c =>
                {
                    var code = c?.Length > 2 ? c[..2] : c;
                    return new[] { "AE", "SA", "QA", "KW", "BH", "OM" }.Contains(code?.ToUpperInvariant());
                })
                .WithMessage("Delivery is only available to GCC countries.");
        });
    }
}
