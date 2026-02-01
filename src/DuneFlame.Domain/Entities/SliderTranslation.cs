using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class SliderTranslation : BaseEntity
{
    public Guid SliderId { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? ButtonText { get; set; }

    // Navigation Property
    public virtual Slider Slider { get; set; } = null!;
}
