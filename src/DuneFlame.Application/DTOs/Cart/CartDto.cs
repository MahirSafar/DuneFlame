namespace DuneFlame.Application.DTOs.Cart;

public record CartDto(
    Guid Id,
    decimal TotalAmount,
    List<CartItemDto> Items
);
