namespace DuneFlame.Domain.Enums;

/// <summary>
/// Describes how the customer intends to pay for the order.
/// Drives the Quiqup delivery payload: pre-paid orders send payment_mode="pre_paid"
/// with payment_amount=0; COD orders send payment_mode="paid_on_delivery" with the
/// full order total so the courier can collect the correct amount at the door.
/// </summary>
public enum PaymentMethod
{
    /// <summary>
    /// Customer paid online (Stripe card / wallet). Quiqup: payment_mode = "pre_paid".
    /// </summary>
    Online = 1,

    /// <summary>
    /// Customer pays the courier at delivery. Quiqup: payment_mode = "paid_on_delivery".
    /// </summary>
    CashOnDelivery = 2
}
