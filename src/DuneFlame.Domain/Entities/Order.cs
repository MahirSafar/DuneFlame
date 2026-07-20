using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace DuneFlame.Domain.Entities;

public class Order : BaseEntity
{
    public Guid? UserId { get; set; }
    public string BasketId { get; set; } = string.Empty;
    public ApplicationUser? ApplicationUser { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    public Currency CurrencyCode { get; set; } = Currency.USD;
    public string ShippingAddress { get; set; } = string.Empty;
    public decimal PointsRedeemed { get; set; } = 0;
    public decimal PointsEarned { get; set; } = 0;
    public string? PaymentIntentId { get; set; }
    public string LanguageCode { get; set; } = "en";
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Sales channel through which the order was placed (Web, Mobile, B2B).
    /// </summary>
    public OrderChannel Channel { get; set; } = OrderChannel.Web;

    /// <summary>
    /// How the customer pays for this order.
    /// Drives the Quiqup delivery payload: Online → payment_mode="pre_paid", payment_amount=0;
    /// CashOnDelivery → payment_mode="paid_on_delivery", payment_amount=order total.
    /// Defaults to Online to keep backward compatibility with existing Stripe-paid orders.
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Online;

    /// <summary>
    /// Human-readable shipping / delivery method name (e.g. "Standard", "Express").
    /// Captured at order creation time from the chosen shipping rate.
    /// </summary>
    public string? ShippingMethodName { get; set; }

    /// <summary>
    /// Delivery/shipping state tracked independently from the fulfillment OrderStatus.
    /// Admin updates this to reflect courier progress.
    /// </summary>
    public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.Pending;

    // ── Quiqup Last-Mile Delivery ─────────────────────────────────────────────

    /// <summary>
    /// Quiqup's internal numeric order ID, assigned when the order is submitted to
    /// their API via <c>IQuiqupDeliveryService.CreateOrderAsync</c>.
    /// Null until the Quiqup order has been successfully created.
    /// Used as the foreign key to correlate inbound webhook notifications.
    /// </summary>
    public long? QuiqupOrderId { get; set; }

    /// <summary>
    /// Quiqup destination-level tracking URL (e.g., https://track-parcel.staging.quiqup.com/...).
    /// Populated alongside <see cref="QuiqupOrderId"/>. Can be surfaced to customers
    /// for real-time shipment tracking.
    /// </summary>
    public string? QuiqupTrackingUrl { get; set; }

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
