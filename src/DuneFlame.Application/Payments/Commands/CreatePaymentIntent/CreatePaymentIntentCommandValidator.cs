using FluentValidation;
using MediatR;

namespace DuneFlame.Application.Payments.Commands.CreatePaymentIntent;

public class CreatePaymentIntentCommandValidator : AbstractValidator<CreatePaymentIntentCommand>
{
    public CreatePaymentIntentCommandValidator()
    {
        RuleFor(x => x.BasketId).NotEmpty().WithMessage("Basket ID is required.");
    }
}
