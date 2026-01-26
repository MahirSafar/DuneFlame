namespace DuneFlame.Application.DTOs.Cart;

public record CartItemDto(
    Guid Id,
    Guid ProductId,
    Guid ProductPriceId,
    string ProductName,
    decimal Price,
    int Quantity,
    string? ImageUrl,
    string WeightLabel,
    int Grams,
    string RoastLevelName,
    string GrindTypeName,
    Guid RoastLevelId,
    Guid GrindTypeId
);
