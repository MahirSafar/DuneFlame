using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Origin : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    // Relationship: 1-to-Many with ProductCoffeeProfile
    public ICollection<ProductCoffeeProfile> Profiles { get; set; } = new List<ProductCoffeeProfile>();
}
