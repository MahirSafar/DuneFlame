using MediatR;

namespace DuneFlame.Application.Baskets.Commands.DeleteBasket;

public record DeleteBasketCommand(string BasketId) : IRequest;
