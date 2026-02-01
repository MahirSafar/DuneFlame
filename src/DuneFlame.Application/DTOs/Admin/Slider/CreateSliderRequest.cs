using Microsoft.AspNetCore.Http;

namespace DuneFlame.Application.DTOs.Admin.Slider;

public class CreateSliderRequest
{
    public IFormFile Image { get; set; } = null!;
    public int Order { get; set; }
    public List<SliderTranslationDto> Translations { get; set; } = new();
}
