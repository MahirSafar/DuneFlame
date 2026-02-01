namespace DuneFlame.Application.DTOs.Admin.Slider;

public class SliderTranslationDto
{
    public string LanguageCode { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Subtitle { get; set; }
    public string? ButtonText { get; set; }
}
