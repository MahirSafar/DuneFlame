using DuneFlame.Application.DTOs.Payment;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CreatePaymentIntentRequestValidator : AbstractValidator<CreatePaymentIntentRequest>
{
    public CreatePaymentIntentRequestValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required.");
    }
}
