using Microsoft.AspNetCore.Http;

namespace DuneFlame.Application.DTOs.Product;

/// <summary>
/// Request to create a new product with multi-language support.
/// 
/// Language Resolution Strategy:
/// 1. If Translations is provided: Each translation is saved with its specified LanguageCode
/// 2. If Translations is null/empty: Name and Description are saved using the Accept-Language header
///    - E.g., Accept-Language: ar → saved as Arabic (ar)
///    - E.g., Accept-Language: en-US → saved as English (en)
///    - Defaults to "en" if header is missing or unsupported
/// 
/// Note: Always normalize Accept-Language header to 2-char format per Copilot Instructions.
/// Example: "en-US" → "en", "ar-SA" → "ar"
/// </summary>
public record CreateProductRequest(
    string Name,
    string Description,
    decimal StockInKg,
    Guid CategoryId,
    Guid? OriginId,
    List<Guid> RoastLevelIds,
    List<Guid> GrindTypeIds,
    List<FlavourNoteCreateDto> FlavourNotes,
    List<ProductPriceCreateDto> Prices,
    List<IFormFile>? Images,
    List<ProductTranslationCreateDto>? Translations = null
);

public record FlavourNoteCreateDto(
    string Name,
    int DisplayOrder = 0,
    List<FlavourNoteTranslationCreateDto>? Translations = null
);

public record FlavourNoteTranslationCreateDto(
    string LanguageCode,
    string Name
);

public record ProductPriceCreateDto(
    Guid ProductWeightId,
    decimal Price,
    string CurrencyCode = "USD"
);

public record ProductTranslationCreateDto(
    string LanguageCode,
    string Name,
    string Description
);