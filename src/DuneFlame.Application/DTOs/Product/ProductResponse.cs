namespace DuneFlame.Application.DTOs.Product;

public record ProductResponse(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    decimal StockInKg,
    bool IsActive,
    Guid CategoryId,
    string CategoryName,
    Guid? OriginId,
    string? OriginName,
    List<string> RoastLevelNames,
    List<string> GrindTypeNames,
    List<Guid> RoastLevelIds,
    List<Guid> GrindTypeIds,
    ProductPriceDto? ActivePrice,  // Single price for current currency
    List<CurrencyOptionDto> OtherAvailableCurrencies,  // Alternative currencies
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<ProductImageDto> Images
);

public record ProductPriceDto(
    Guid ProductPriceId,
    string WeightLabel,
    int Grams,
    decimal Price,
    string CurrencyCode
);

public record CurrencyOptionDto(
    string CurrencyCode,
    string WeightLabel,
    int Grams,
    decimal Price,
    Guid ProductPriceId
);

public record ProductImageDto(
    Guid Id,
    string ImageUrl,
    bool IsMain
);
