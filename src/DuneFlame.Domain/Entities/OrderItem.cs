using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Domain.Entities;

public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    // Variant Engine
    public Guid ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public Currency CurrencyCode { get; set; } = Currency.USD;

    public string? SelectedRoastLevelName { get; set; } // Məs: "Medium Roast"
    public string? SelectedGrindTypeName { get; set; }  // Məs: "Espresso"
}
