using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Order;

public record OrderDto(
    Guid Id,
    OrderStatus Status,
    decimal TotalAmount,
    DateTime CreatedAt,
    string ShippingAddress,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string? PaymentTransactionId,
    List<OrderItemDto> Items
);
