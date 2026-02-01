using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Product : BaseEntity
{
    public string Slug { get; set; } = string.Empty;
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
    public ICollection<ProductTranslation> Translations { get; set; } = [];
    public ICollection<FlavourNote> FlavourNotes { get; set; } = [];

    // M2M Relationships
    public ICollection<RoastLevelEntity> RoastLevels { get; set; } = [];
    public ICollection<GrindType> GrindTypes { get; set; } = [];
}