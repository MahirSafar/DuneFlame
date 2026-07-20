using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Baskets.Commands.UpdateBasket;

public class UpdateBasketCommandHandler(IBasketService basketService)
    : IRequestHandler<UpdateBasketCommand>
{
    public Task Handle(UpdateBasketCommand command, CancellationToken cancellationToken)
        => basketService.UpdateBasketAsync(command.Basket, command.SaveChanges);
}
