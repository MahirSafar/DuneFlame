namespace DuneFlame.Application.DTOs.Cart;

public record CartItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal Price,
    int Quantity,
    string? ImageUrl
);
