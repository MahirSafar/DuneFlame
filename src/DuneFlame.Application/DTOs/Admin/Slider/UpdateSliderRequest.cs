using Microsoft.AspNetCore.Http;

namespace DuneFlame.Application.DTOs.Admin.Slider;

public class UpdateSliderRequest
{
    public IFormFile? Image { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SliderTranslationDto> Translations { get; set; } = new();
}
