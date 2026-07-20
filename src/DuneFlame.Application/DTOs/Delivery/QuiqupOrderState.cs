namespace DuneFlame.Application.DTOs.Delivery;

/// <summary>
/// String constants for the <c>filters[state]</c> query parameter
/// accepted by GET /orders on the Quiqup Ecommerce API.
/// 
/// Use these constants with <see cref="Interfaces.IQuiqupDeliveryService.ListOrdersAsync"/>
/// to avoid magic strings and keep filtering logic maintainable.
/// </summary>
public static class QuiqupOrderState
{
    // ── Draft ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Order created in draft. Quiqup ignores it until submitted.
    /// Applies to both forward and reverse shipments.
    /// </summary>
    public const string Pending = "pending";

    // ── Active (Pre-Collection) ────────────────────────────────────────────────

    /// <summary>Order confirmed, queued for the next collection run.</summary>
    public const string ReadyForCollection = "ready_for_collection";

    /// <summary>Courier assigned and en route to collect the parcel.</summary>
    public const string OutForCollection = "out_for_collection";

    /// <summary>Collection attempted but unsuccessful. Includes a failure reason.</summary>
    public const string CollectionFailed = "collection_failed";

    // ── In Transit ─────────────────────────────────────────────────────────────

    /// <summary>Parcel collected from origin and in transit to the Quiqup depot.</summary>
    public const string Collected = "collected";

    /// <summary>Parcel received and logged at the depot.</summary>
    public const string ReceivedAtDepot = "received_at_depot";

    /// <summary>Parcel is in transit between Dubai and Abu-Dhabi warehouses.</summary>
    public const string Transit = "Transit";

    /// <summary>Parcel awaiting scheduling or next delivery day at the depot.</summary>
    public const string AtDepot = "at_depot";

    /// <summary>Delivery scheduled with the customer for a future date.</summary>
    public const string Scheduled = "scheduled";

    // ── Delivery ───────────────────────────────────────────────────────────────

    /// <summary>Courier is out performing the delivery.</summary>
    public const string OutForDelivery = "out_for_delivery";

    /// <summary>
    /// Delivery succeeded. Terminal / end-of-lifecycle state.
    /// Marked with † in the Quiqup documentation.
    /// </summary>
    public const string DeliveryComplete = "delivery_complete";

    /// <summary>Delivery failed. Includes a failure reason.</summary>
    public const string DeliveryFailed = "delivery_failed";

    // ── Returns (Forward Shipments Only) ──────────────────────────────────────

    /// <summary>Parcel flagged for return to the sender. Forward shipments only.</summary>
    public const string ReturnToOrigin = "return_to_origin";

    /// <summary>Return courier dispatched. Forward shipments only.</summary>
    public const string OutForReturn = "out_for_return";

    /// <summary>
    /// Parcel returned to sender. Terminal / end-of-lifecycle state.
    /// Marked with † in the Quiqup documentation. Forward shipments only.
    /// </summary>
    public const string ReturnedToOrigin = "returned_to_origin";

    // ── Exception States ───────────────────────────────────────────────────────

    /// <summary>Delivery cancelled by sender or Quiqup.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Delivery put on hold pending resolution.</summary>
    public const string OnHold = "on_hold";
}
