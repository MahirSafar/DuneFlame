namespace DuneFlame.Application.DTOs.Admin;

public record SliderDto(
    Guid Id,
    string Title,
    string Subtitle,
    string ImageUrl,
    string TargetUrl,
    int Order,
    bool IsActive
);
