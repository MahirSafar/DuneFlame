using DuneFlame.Application.DTOs.Payment;
using MediatR;

namespace DuneFlame.Application.Payments.Commands.CreatePaymentIntent;

/// <summary>Creates or updates a Stripe PaymentIntent for a basket, applying welcome discount if applicable.</summary>
public record CreatePaymentIntentCommand(
    string BasketId,
    Guid? UserId
) : IRequest<PaymentIntentDto>;
