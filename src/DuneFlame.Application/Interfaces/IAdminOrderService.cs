using DuneFlame.Application.DTOs.Order;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.Interfaces;

public interface IAdminOrderService
{
    Task<List<OrderDto>> GetAllOrdersAsync();
    Task UpdateOrderStatusAsync(Guid orderId, OrderStatus status);
    Task CancelOrderAsync(Guid orderId);
}
