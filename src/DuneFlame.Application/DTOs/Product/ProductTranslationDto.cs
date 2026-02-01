namespace DuneFlame.Application.DTOs.Product;

/// <summary>
/// Data Transfer Object for Product translations.
/// Used to expose multilingual product content to API clients.
/// </summary>
public record ProductTranslationDto(
    string LanguageCode,
    string Name,
    string Description
);
