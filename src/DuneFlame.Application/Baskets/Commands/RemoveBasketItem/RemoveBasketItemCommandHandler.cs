using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Baskets.Commands.RemoveBasketItem;

public class RemoveBasketItemCommandHandler(IBasketService basketService)
    : IRequestHandler<RemoveBasketItemCommand>
{
    public Task Handle(RemoveBasketItemCommand command, CancellationToken cancellationToken)
        => basketService.RemoveItemFromBasketAsync(command.BasketId, command.ItemId);
}
