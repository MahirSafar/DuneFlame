using DuneFlame.Application.DTOs.Order;
using MediatR;

namespace DuneFlame.Application.Orders.Queries.GetMyOrders;

public record GetMyOrdersQuery(Guid UserId) : IRequest<List<OrderDto>>;
