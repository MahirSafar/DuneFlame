using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal StockInKg { get; set; } // Central Silo Stock
    public bool IsActive { get; set; } = true;

    // Foreign Keys
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    public Guid? OriginId { get; set; }
    public Origin? Origin { get; set; }

    // Navigation Properties
    public ICollection<ProductImage> Images { get; set; } = [];
    public ICollection<ProductPrice> Prices { get; set; } = [];
    
    // M2M Relationships
    public ICollection<RoastLevelEntity> RoastLevels { get; set; } = [];
    public ICollection<GrindType> GrindTypes { get; set; } = [];
}