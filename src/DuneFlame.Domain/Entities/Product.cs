using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; } // Endirim varsa köhnə qiymət
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;

    // Foreign Key
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    // Images Relationship (1-to-Many)
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}