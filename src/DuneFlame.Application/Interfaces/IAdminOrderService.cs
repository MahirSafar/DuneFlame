using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Order;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.Interfaces;

public interface IAdminOrderService
{
    Task<PagedResult<AdminOrderListDto>> GetAllOrdersAsync(
        int pageNumber = 1,
        int pageSize = 10,
        OrderStatus? status = null,
        string? searchTerm = null);
    Task UpdateOrderStatusAsync(Guid orderId, OrderStatus status);
    Task UpdateDeliveryStatusAsync(Guid orderId, DeliveryStatus deliveryStatus);
    Task CancelOrderAsync(Guid orderId);
}
