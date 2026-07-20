using System.Text.Json.Serialization;

namespace DuneFlame.Infrastructure.DTOs.Quiqup;

/// <summary>
/// JSON request body for PUT /orders/{order_id}.
/// Only <c>payment_mode</c> and <c>payment_amount</c> are currently supported by Quiqup,
/// and they must always be provided together.
/// </summary>
/// <remarks>
/// Business rule enforced by Quiqup:
/// <list type="bullet">
///   <item><description>When <c>payment_mode</c> is <c>pre_paid</c>, <c>payment_amount</c> must be <c>0.0</c>.</description></item>
///   <item><description>For all other payment modes, <c>payment_amount</c> must be a positive number.</description></item>
/// </list>
/// This rule is validated in <c>QuiqupDeliveryService.UpdatePendingOrderPaymentAsync</c>
/// before the HTTP request is sent.
/// </remarks>
public class QuiqupUpdateOrderRequest
{
    /// <summary>
    /// The payment mode to apply to the order.
    /// Use the <see cref="Application.DTOs.Delivery.QuiqupPaymentMode"/> constants
    /// (e.g., <c>QuiqupPaymentMode.PrePaid</c>, <c>QuiqupPaymentMode.CashOnDelivery</c>).
    /// </summary>
    [JsonPropertyName("payment_mode")]
    public string PaymentMode { get; set; } = string.Empty;

    /// <summary>
    /// The payment amount. Must be exactly <c>0.0</c> when <see cref="PaymentMode"/>
    /// is <c>pre_paid</c>; must be positive for all COD/POD variants.
    /// </summary>
    [JsonPropertyName("payment_amount")]
    public double PaymentAmount { get; set; }
}
