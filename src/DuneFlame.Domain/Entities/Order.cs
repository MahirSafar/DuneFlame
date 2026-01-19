using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Domain.Entities;

public class Order : BaseEntity
{
    public Guid UserId { get; set; }
    public ApplicationUser? ApplicationUser { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    public string ShippingAddress { get; set; } = string.Empty;
    public decimal PointsRedeemed { get; set; } = 0;
    public decimal PointsEarned { get; set; } = 0;
    public string? PaymentIntentId { get; set; }
    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = [];
}
