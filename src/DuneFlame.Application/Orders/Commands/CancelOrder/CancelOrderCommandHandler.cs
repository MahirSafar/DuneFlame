using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Orders.Commands.CancelOrder;

public class CancelOrderCommandHandler(IOrderService orderService)
    : IRequestHandler<CancelOrderCommand>
{
    public Task Handle(CancelOrderCommand command, CancellationToken cancellationToken)
        => orderService.CancelAbandonedOrderAsync(command.OrderId);
}
