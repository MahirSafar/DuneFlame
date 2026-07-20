namespace DuneFlame.Domain.Enums;

/// <summary>
/// Represents the delivery/shipping state of an order, tracked independently
/// from the overall OrderStatus fulfillment state.
/// Values 0–3 are pre-existing; values 4–7 map to Quiqup courier lifecycle states.
/// </summary>
public enum DeliveryStatus
{
    // ── Pre-existing states ───────────────────────────────────────────────────
    Pending             = 0,  // Not yet shipped / awaiting fulfilment
    InTransit           = 1,  // Shipped, on the way to the customer
    Delivered           = 2,  // Successfully delivered
    Returned            = 3,  // Returned by customer or carrier

    // ── Quiqup courier lifecycle states ───────────────────────────────────────
    ReadyForCollection  = 4,  // Order confirmed; Quiqup courier notified for pickup
    PickedUp            = 5,  // Courier collected the parcel from our warehouse
    Cancelled           = 6,  // Order cancelled via Quiqup (PUT /orders/batch/set_cancelled)
    Failed              = 7,  // Delivery attempt failed or unrecognised Quiqup state
}
