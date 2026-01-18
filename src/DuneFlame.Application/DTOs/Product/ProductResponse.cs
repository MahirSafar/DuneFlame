using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Product;

public record ProductResponse(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    decimal Price,
    decimal DiscountPercentage,
    int StockQuantity,
    bool IsActive,
    Guid CategoryId,
    string CategoryName,
    Guid? OriginId,
    string? OriginName,
    RoastLevel RoastLevel,
    int Weight,
    string FlavorNotes,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<ProductImageDto> Images
);

public record ProductImageDto(
    Guid Id,
    string ImageUrl,
    bool IsMain
);
