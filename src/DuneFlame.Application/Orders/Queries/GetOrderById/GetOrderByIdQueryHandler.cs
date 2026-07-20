using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Orders.Queries.GetOrderById;

public class GetOrderByIdQueryHandler(IOrderService orderService)
    : IRequestHandler<GetOrderByIdQuery, OrderDto>
{
    public Task<OrderDto> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
        => orderService.GetOrderByIdAsync(query.OrderId);
}
