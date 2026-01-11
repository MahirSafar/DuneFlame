namespace DuneFlame.Application.DTOs.Admin;

public record CreateSliderRequest(
    string Title,
    string Subtitle,
    string ImageUrl,
    string TargetUrl,
    int Order,
    bool IsActive = true
);
