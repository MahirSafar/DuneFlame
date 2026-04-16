namespace DuneFlame.Application.DTOs.Product;

/// <summary>
/// API Response DTO for product data with multi-language support.
/// 
/// The Name and Description fields contain the current language translation
/// (determined by Accept-Language header). To access translations in other languages,
/// use the Translations collection.
/// 
/// Example:
/// - Accept-Language: ar → Name, Description in Arabic (from Translations list)
/// - Accept-Language: en → Name, Description in English (from Translations list)
/// - Other languages → Fallback to English, then first available translation
/// </summary>
public record ProductResponse(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    bool IsActive,
    Guid CategoryId,
    string CategoryName,
    Guid? BrandId,
    string? BrandName,
    List<ProductTranslationDto> Translations,  // ← ALL available translations
    ProductCoffeeProfileDto? CoffeeProfile,
    Dictionary<string, string>? Specifications,
    List<VariantDto> Variants,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<ProductImageDto> Images
);

public record ProductCoffeeProfileDto(
    Guid? OriginId,
    string? OriginName,
    List<string> RoastLevelNames,
    List<string> GrindTypeNames,
    List<Guid> RoastLevelIds,
    List<Guid> GrindTypeIds,
    List<FlavourNoteDto> FlavourNotes
);

public record VariantDto(
    Guid Id,
    string Sku,
    decimal Price,
    int? StockQuantity,
    List<VariantOptionDto> Options,
    List<VariantPriceDto> Prices
);

public record VariantPriceDto(
    string CurrencyCode,
    decimal Price
);

public record VariantOptionDto(
    string AttributeName,
    string Value
);

public record ProductImageDto(
    Guid Id,
    string ImageUrl,
    bool IsMain
);
