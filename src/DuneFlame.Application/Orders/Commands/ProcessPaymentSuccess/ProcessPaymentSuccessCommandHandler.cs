using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Orders.Commands.ProcessPaymentSuccess;

public class ProcessPaymentSuccessCommandHandler(IOrderService orderService)
    : IRequestHandler<ProcessPaymentSuccessCommand>
{
    public Task Handle(ProcessPaymentSuccessCommand command, CancellationToken cancellationToken)
        => orderService.ProcessPaymentSuccessAsync(command.TransactionId);
}
