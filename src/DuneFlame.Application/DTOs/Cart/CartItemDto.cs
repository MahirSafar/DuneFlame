namespace DuneFlame.Application.DTOs.Cart;

public record CartItemDto(
    Guid Id,
    Guid ProductId,
    Guid ProductVariantId,
    string ProductName,
    decimal Price,
    int Quantity,
    string? ImageUrl,
    string Sku,
    List<string> Attributes, // e.g. "Weight: 250g", "Color: Black"
    string? RoastLevelName,
    string? GrindTypeName,
    Guid? RoastLevelId,
    Guid? GrindTypeId
);
