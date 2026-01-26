using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Order;

public record OrderDto(
    Guid Id,
    OrderStatus Status,
    decimal TotalAmount,
    Currency Currency,
    DateTime CreatedAt,
    string ShippingAddress,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string? PaymentTransactionId,
    string? PaymentIntentId,
    string? ClientSecret,
    List<OrderItemDto> Items
);
