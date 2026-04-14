using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ProductCoffeeProfile : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public Guid? OriginId { get; set; }
    public Origin? Origin { get; set; }

    public ICollection<FlavourNote> FlavourNotes { get; set; } = [];
    public ICollection<RoastLevelEntity> RoastLevels { get; set; } = [];
    public ICollection<GrindType> GrindTypes { get; set; } = [];
}
