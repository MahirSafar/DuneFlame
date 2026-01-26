using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Domain.Entities;

public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid ProductPriceId { get; set; }
    public ProductPrice? ProductPrice { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public Currency CurrencyCode { get; set; } = Currency.USD;
}
