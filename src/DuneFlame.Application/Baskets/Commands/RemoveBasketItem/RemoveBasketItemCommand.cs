using MediatR;

namespace DuneFlame.Application.Baskets.Commands.RemoveBasketItem;

public record RemoveBasketItemCommand(string BasketId, Guid ItemId) : IRequest;
