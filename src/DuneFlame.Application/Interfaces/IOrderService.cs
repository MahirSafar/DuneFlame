using DuneFlame.Application.DTOs.Order;

namespace DuneFlame.Application.Interfaces;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(Guid? userId, CreateOrderRequest request);
    Task<List<OrderDto>> GetMyOrdersAsync(Guid userId);
    Task<OrderDto> GetOrderByIdAsync(Guid id);
    Task ProcessPaymentSuccessAsync(string transactionId);
}
