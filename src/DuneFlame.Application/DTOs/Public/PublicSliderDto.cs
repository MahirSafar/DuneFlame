namespace DuneFlame.Application.DTOs.Public;

public record PublicSliderDto(
    Guid Id,
    string ImageUrl,
    int Order,
    string Title,
    string? Subtitle,
    string? ButtonText,
    string? LinkUrl
);
