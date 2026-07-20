using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Orders.Queries.GetMyOrders;

public class GetMyOrdersQueryHandler(IOrderService orderService)
    : IRequestHandler<GetMyOrdersQuery, List<OrderDto>>
{
    public Task<List<OrderDto>> Handle(GetMyOrdersQuery query, CancellationToken cancellationToken)
        => orderService.GetMyOrdersAsync(query.UserId);
}
