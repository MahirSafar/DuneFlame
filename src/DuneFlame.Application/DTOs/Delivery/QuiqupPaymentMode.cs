namespace DuneFlame.Application.DTOs.Delivery;

/// <summary>
/// String constants for the <c>payment_mode</c> field accepted by
/// PUT /orders/{order_id} on the Quiqup Ecommerce API.
///
/// Use these with <see cref="Interfaces.IQuiqupDeliveryService.UpdatePendingOrderPaymentAsync"/>
/// to avoid magic strings.
/// </summary>
/// <remarks>
/// <b>Business rule</b>: When <see cref="PrePaid"/> is used, the <c>payment_amount</c>
/// must be exactly <c>0.0</c>. For all other modes it must be a positive number.
/// </remarks>
public static class QuiqupPaymentMode
{
    /// <summary>
    /// Payment was collected in advance (e.g., via Stripe at checkout).
    /// Requires <c>payment_amount = 0.0</c>.
    /// This is the standard mode for all DuneFlame Stripe-based orders.
    /// </summary>
    public const string PrePaid = "pre_paid";

    /// <summary>
    /// Courier collects cash from the customer at the door upon delivery.
    /// Requires a positive <c>payment_amount</c>.
    /// </summary>
    public const string CashOnDelivery = "cash_on_delivery";

    /// <summary>
    /// Courier presents a card terminal to the customer at the door upon delivery.
    /// Requires a positive <c>payment_amount</c>.
    /// </summary>
    public const string CardOnDelivery = "card_on_delivery";

    /// <summary>
    /// Generic on-delivery payment mode.
    /// Requires a positive <c>payment_amount</c>.
    /// </summary>
    public const string PaidOnDelivery = "paid_on_delivery";
}
