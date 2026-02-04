using DuneFlame.Application.DTOs.Order;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.BasketId)
            .NotEmpty()
            .WithMessage("Basket ID is required.");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .WithMessage("Currency is required.")
            .Must(x => x?.Equals("USD", StringComparison.OrdinalIgnoreCase) == true || 
                       x?.Equals("AED", StringComparison.OrdinalIgnoreCase) == true)
            .WithMessage("Currency must be either 'USD' or 'AED'.");

        RuleFor(x => x.ShippingAddress)
            .NotNull()
            .WithMessage("Shipping address is required.");

        When(x => x.ShippingAddress != null, () =>
        {
            RuleFor(x => x.ShippingAddress.Street)
                .NotEmpty()
                .WithMessage("Street address is required.")
                .MinimumLength(5)
                .WithMessage("Street address must be at least 5 characters long.")
                .MaximumLength(200)
                .WithMessage("Street address must not exceed 200 characters.");

            RuleFor(x => x.ShippingAddress.City)
                .NotEmpty()
                .WithMessage("City is required.")
                .MinimumLength(2)
                .WithMessage("City must be at least 2 characters long.")
                .MaximumLength(50)
                .WithMessage("City must not exceed 50 characters.");

            RuleFor(x => x.ShippingAddress.State)
                .NotEmpty()
                .WithMessage("State/Province is required.")
                .MinimumLength(2)
                .WithMessage("State must be at least 2 characters long.")
                .MaximumLength(50)
                .WithMessage("State must not exceed 50 characters.");

            RuleFor(x => x.ShippingAddress.PostalCode)
                .NotEmpty()
                .WithMessage("Postal code is required.")
                .MinimumLength(3)
                .WithMessage("Postal code must be at least 3 characters long.")
                .MaximumLength(20)
                .WithMessage("Postal code must not exceed 20 characters.");

            RuleFor(x => x.ShippingAddress.Country)
                .NotEmpty()
                .WithMessage("Country is required.")
                .MinimumLength(2)
                .WithMessage("Country must be at least 2 characters long.")
                .MaximumLength(50)
                .WithMessage("Country must not exceed 50 characters.");

            // Guest contact information validation (optional for logged-in users, required for guests)
            RuleFor(x => x.ShippingAddress.Email)
                .EmailAddress()
                .WithMessage("Valid email address is required if provided.");

            RuleFor(x => x.ShippingAddress.PhoneNumber)
                .MinimumLength(7)
                .WithMessage("Phone number must be at least 7 characters long.")
                .MaximumLength(20)
                .WithMessage("Phone number must not exceed 20 characters.")
                .When(x => !string.IsNullOrEmpty(x.ShippingAddress.PhoneNumber));

            RuleFor(x => x.ShippingAddress.FirstName)
                .MinimumLength(2)
                .WithMessage("First name must be at least 2 characters long.")
                .MaximumLength(50)
                .WithMessage("First name must not exceed 50 characters.")
                .When(x => !string.IsNullOrEmpty(x.ShippingAddress.FirstName));

            RuleFor(x => x.ShippingAddress.LastName)
                .MinimumLength(2)
                .WithMessage("Last name must be at least 2 characters long.")
                .MaximumLength(50)
                .WithMessage("Last name must not exceed 50 characters.")
                .When(x => !string.IsNullOrEmpty(x.ShippingAddress.LastName));
        });
    }
}

