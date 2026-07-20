using DuneFlame.Application.DTOs.Basket;
using MediatR;

namespace DuneFlame.Application.Baskets.Queries.GetBasket;

public record GetBasketQuery(string BasketId) : IRequest<CustomerBasketDto>;
