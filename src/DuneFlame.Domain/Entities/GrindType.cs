using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class GrindType : BaseEntity
{
    public string Name { get; set; } = string.Empty; // e.g., "Whole Bean", "Espresso", "Filter"

    // Navigation (M2M with ProductCoffeeProfile)
    public ICollection<ProductCoffeeProfile> Profiles { get; set; } = [];
}
