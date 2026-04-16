using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Product : BaseEntity
{
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Foreign Keys
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    public Guid? BrandId { get; set; }
    public Brand? Brand { get; set; }

    // Navigation Properties
    public ICollection<ProductImage> Images { get; set; } = [];
    public ICollection<ProductTranslation> Translations { get; set; } = [];

    // Phase 1 - Additive Architecture
    public ProductCoffeeProfile? CoffeeProfile { get; set; }
    public ProductEquipmentProfile? EquipmentProfile { get; set; }
    public ICollection<ProductVariant> Variants { get; set; } = [];
}