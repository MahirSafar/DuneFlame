namespace DuneFlame.Application.DTOs.Admin.Slider;

public record SliderResponse(
    Guid Id,
    string ImageUrl,
    int Order,
    bool IsActive,
    List<SliderTranslationResponseDto> Translations,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record SliderTranslationResponseDto(
    Guid Id,
    string LanguageCode,
    string Title,
    string? Subtitle,
    string? ButtonText
);
