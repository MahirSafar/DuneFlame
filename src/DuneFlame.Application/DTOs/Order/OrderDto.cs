using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Order;

public record OrderDto(
    Guid Id,
    OrderStatus Status,
    decimal TotalAmount,
    DateTime CreatedAt,
    List<OrderItemDto> Items
);
