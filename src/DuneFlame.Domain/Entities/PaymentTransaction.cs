using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Domain.Entities;

public class PaymentTransaction : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Currency CurrencyCode { get; set; } = Currency.USD;
    public string Status { get; set; } = "Pending";
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// Refund ID from payment gateway (Stripe charge ID for refunds).
    /// Used for idempotency: prevents duplicate refund processing.
    /// </summary>
    public string? RefundId { get; set; }
}
