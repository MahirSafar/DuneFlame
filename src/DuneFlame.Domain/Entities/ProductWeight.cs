using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ProductWeight : BaseEntity
{
    public string Label { get; set; } = string.Empty; // e.g., "250g", "1kg"
    public int Grams { get; set; } // e.g., 250, 1000

    // Navigation
    public ICollection<ProductPrice> Prices { get; set; } = [];
}
