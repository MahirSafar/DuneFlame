namespace DuneFlame.Application.DTOs.Product;

public record FlavourNoteDto(
    Guid Id,
    string Name,
    int DisplayOrder,
    List<FlavourNoteTranslationDto> Translations
);
