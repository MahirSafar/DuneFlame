namespace DuneFlame.Application.DTOs.Cart;

public record AddToCartRequest(
    Guid ProductId,
    int Quantity
);
