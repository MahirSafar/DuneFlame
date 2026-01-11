namespace DuneFlame.Application.DTOs.Admin;

public record CreateAboutSectionRequest(
    string Title,
    string Content,
    string ImageUrl
);
