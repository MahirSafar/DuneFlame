using DuneFlame.Application.DTOs.Basket;
using MediatR;

namespace DuneFlame.Application.Baskets.Commands.UpdateBasket;

public record UpdateBasketCommand(CustomerBasketDto Basket, bool SaveChanges = true) : IRequest;
