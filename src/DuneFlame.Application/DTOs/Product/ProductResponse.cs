namespace DuneFlame.Application.DTOs.Product;

public record ProductResponse(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    decimal? OldPrice,
    int StockQuantity,
    bool IsActive,
    Guid CategoryId,
    string CategoryName,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<ProductImageDto> Images
);

public record ProductImageDto(
    Guid Id,
    string ImageUrl,
    bool IsMain
);
