namespace DuneFlame.Application.DTOs.Product;

public record FlavourNoteTranslationDto(
    Guid FlavourNoteId,
    string LanguageCode,
    string Name
);
