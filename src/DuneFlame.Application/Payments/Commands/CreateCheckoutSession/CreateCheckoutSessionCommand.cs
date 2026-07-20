using MediatR;

namespace DuneFlame.Application.Payments.Commands.CreateCheckoutSession;

public record CreateCheckoutSessionCommand(
    string ItemCode,
    decimal Quantity,
    string SuccessUrl,
    string CancelUrl
) : IRequest<string>;
