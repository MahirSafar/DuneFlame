using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Domain.Entities;

public class ProductPrice : BaseEntity
{
    public Guid ProductId { get; set; }
    public Guid ProductWeightId { get; set; }
    public decimal Price { get; set; }
    public Currency CurrencyCode { get; set; } = Currency.USD;

    // Navigation
    public Product? Product { get; set; }
    public ProductWeight? Weight { get; set; }
}
