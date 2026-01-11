namespace DuneFlame.Application.DTOs.Admin;

public record AboutSectionDto(
    Guid Id,
    string Title,
    string Content,
    string ImageUrl
);
