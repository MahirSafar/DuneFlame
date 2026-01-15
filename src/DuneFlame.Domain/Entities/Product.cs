using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Domain.Entities;

public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; } // Base/Sticker Price
    public decimal DiscountPercentage { get; set; } // Discount percentage (0-100)
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;

    // Coffee-specific properties
    public RoastLevel RoastLevel { get; set; } = RoastLevel.None;
    public int Weight { get; set; } // Weight in grams (e.g., 250, 500, 1000)
    public string FlavorNotes { get; set; } = string.Empty; // e.g., "Chocolate, Berry"

    // Foreign Keys
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    public Guid? OriginId { get; set; }
    public Origin? Origin { get; set; }

    // Images Relationship (1-to-Many)
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}