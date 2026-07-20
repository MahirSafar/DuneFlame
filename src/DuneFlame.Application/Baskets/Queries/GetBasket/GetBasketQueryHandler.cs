using DuneFlame.Application.DTOs.Basket;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Baskets.Queries.GetBasket;

public class GetBasketQueryHandler(IBasketService basketService)
    : IRequestHandler<GetBasketQuery, CustomerBasketDto>
{
    public Task<CustomerBasketDto> Handle(GetBasketQuery query, CancellationToken cancellationToken)
        => basketService.GetBasketAsync(query.BasketId);
}
