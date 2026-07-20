using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Payments.Commands.CreateCheckoutSession;

public class CreateCheckoutSessionCommandHandler(IPaymentService paymentService)
    : IRequestHandler<CreateCheckoutSessionCommand, string>
{
    public Task<string> Handle(CreateCheckoutSessionCommand command, CancellationToken cancellationToken)
        => paymentService.CreateCheckoutSessionAsync(
            command.ItemCode,
            command.Quantity,
            Guid.NewGuid(), // Temporary order ID for tracking
            basketId: null,
            command.SuccessUrl,
            command.CancelUrl);
}
