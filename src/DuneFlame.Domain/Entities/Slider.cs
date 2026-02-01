using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Slider : BaseEntity
{
    public string ImageUrl { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation Properties
    public virtual ICollection<SliderTranslation> Translations { get; set; } = new List<SliderTranslation>();
}
