using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ProductVariant : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? StockQuantity { get; set; }

    public ICollection<ProductVariantOption> Options { get; set; } = [];
    public ICollection<ProductVariantPrice> Prices { get; set; } = [];
}


