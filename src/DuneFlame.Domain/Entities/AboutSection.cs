using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class AboutSection : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public ICollection<AboutSectionTranslation> Translations { get; set; } = new List<AboutSectionTranslation>();
}