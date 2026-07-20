using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ProductVariant : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? StockQuantity { get; set; }

    /// <summary>
    /// Physical weight of this variant in kilograms (e.g., 0.25 for a 250 g bag).
    /// Used when building the Quiqup delivery parcel payload.
    /// Null means the weight has not been configured — the delivery service falls back
    /// to a per-category default (0.5 kg for coffee).
    /// </summary>
    public double? WeightKg { get; set; }

    public ICollection<ProductVariantOption> Options { get; set; } = [];
    public ICollection<ProductVariantPrice> Prices { get; set; } = [];
}


