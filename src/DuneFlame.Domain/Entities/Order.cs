using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace DuneFlame.Domain.Entities;

public class Order : BaseEntity
{
    public Guid UserId { get; set; }
    public ApplicationUser? ApplicationUser { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    public Currency CurrencyCode { get; set; } = Currency.USD;
    public string ShippingAddress { get; set; } = string.Empty;
    public decimal PointsRedeemed { get; set; } = 0;
    public decimal PointsEarned { get; set; } = 0;
    public string? PaymentIntentId { get; set; }
    public string LanguageCode { get; set; } = "en";

    /// <summary>
    /// Concurrency control: auto-incremented by database on every update.
    /// Prevents lost updates due to concurrent modifications.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = [];

    /// <summary>
    /// Validates that all items in the order use the same currency.
    /// </summary>
    public void ValidateCurrencyConsistency()
    {
        if (Items.Count == 0)
            return;

        var firstItemCurrency = Items.First().CurrencyCode;
        if (!Items.All(item => item.CurrencyCode == firstItemCurrency))
        {
            throw new InvalidOperationException(
                "Order contains items in different currencies. All items must use the same currency.");
        }

        if (firstItemCurrency != CurrencyCode)
        {
            throw new InvalidOperationException(
                $"Order currency {CurrencyCode} does not match item currency {firstItemCurrency}.");
        }
    }
}
