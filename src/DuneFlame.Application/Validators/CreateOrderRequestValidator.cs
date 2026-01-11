using DuneFlame.Application.DTOs.Order;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.ShippingAddress)
            .NotEmpty()
            .WithMessage("Shipping address is required.")
            .MinimumLength(5)
            .WithMessage("Shipping address must be at least 5 characters long.")
            .MaximumLength(500)
            .WithMessage("Shipping address must not exceed 500 characters.");
    }
}
