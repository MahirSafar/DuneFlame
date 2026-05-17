using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class RoastLevelEntity : BaseEntity
{
    public string Name { get; set; } = string.Empty; // e.g., "Light", "Medium", "Dark"

    // Navigation (M2M with ProductCoffeeProfile)
    public ICollection<ProductCoffeeProfile> Profiles { get; set; } = [];
    public ICollection<RoastLevelTranslation> Translations { get; set; } = new List<RoastLevelTranslation>();
}
