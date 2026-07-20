using DuneFlame.Application.DTOs.Order;
using MediatR;

namespace DuneFlame.Application.Orders.Queries.GetOrderById;

public record GetOrderByIdQuery(Guid OrderId) : IRequest<OrderDto>;
