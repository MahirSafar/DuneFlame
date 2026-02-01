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
    List<ProductTranslationDto> Translations,  // ← ALL available translations
    List<FlavourNoteDto> FlavourNotes,
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
