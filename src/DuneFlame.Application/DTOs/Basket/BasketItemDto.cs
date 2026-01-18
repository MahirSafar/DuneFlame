namespace DuneFlame.Application.DTOs.Basket;

public record BasketItemDto(
    Guid ProductId,
    string ProductName,
    string Slug,
    decimal Price,
    int Quantity,
    string ImageUrl
);
