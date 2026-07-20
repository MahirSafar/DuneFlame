using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Baskets.Commands.DeleteBasket;

public class DeleteBasketCommandHandler(IBasketService basketService)
    : IRequestHandler<DeleteBasketCommand>
{
    public Task Handle(DeleteBasketCommand command, CancellationToken cancellationToken)
        => basketService.DeleteBasketAsync(command.BasketId);
}
