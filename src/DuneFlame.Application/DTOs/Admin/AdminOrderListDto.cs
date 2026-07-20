using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Admin;

/// <summary>
/// Flat DTO optimised for the Admin Orders table view.
/// Contains all columns required by the UI grid.
/// </summary>
public record AdminOrderListDto(
    /// <summary>Order unique identifier.</summary>
    Guid Id,

    /// <summary>Short human-readable order number (first 8 chars of the GUID, upper-cased).</summary>
    string OrderNumber,

    /// <summary>UTC timestamp when the order was created.</summary>
    DateTime CreatedAt,

    // ── Customer ────────────────────────────────────────────────────────────
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,

    // ── Channel ─────────────────────────────────────────────────────────────
    /// <summary>Sales channel: Web=0, Mobile=1, B2B=2. Label: "Website" / "App" / "B2B".</summary>
    OrderChannel Channel,

    // ── Financials ──────────────────────────────────────────────────────────
    decimal TotalAmount,
    Currency Currency,

    // ── Payment ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Normalised payment status label:
    /// "Paid" | "Pending" | "Failed" | "Refunded"
    /// </summary>
    string PaymentStatus,
    string? PaymentIntentId,

    // ── Fulfillment ──────────────────────────────────────────────────────────
    /// <summary>
    /// Overall order fulfillment status (OrderStatus enum).
    /// Frontend maps to: Unfulfilled / Processing / Fulfilled / Cancelled.
    /// </summary>
    OrderStatus FulfillmentStatus,

    // ── Items ────────────────────────────────────────────────────────────────
    /// <summary>Total quantity of all items in the order.</summary>
    int ItemsCount,

    // ── Delivery ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Delivery/shipping state (DeliveryStatus enum), editable independently.
    /// Pre-existing: Pending=0, InTransit=1, Delivered=2, Returned=3.
    /// Quiqup states: ReadyForCollection=4, PickedUp=5, Cancelled=6, Failed=7.
    /// </summary>
    DeliveryStatus DeliveryStatus,

    /// <summary>Human-readable shipping method name (e.g. "Standard Delivery", "Express").</summary>
    string? DeliveryMethod,

    // ── Quiqup Last-Mile ─────────────────────────────────────────────────────
    /// <summary>
    /// Quiqup's internal numeric order ID.
    /// Null if the order has not yet been submitted to Quiqup (Phase 4 not yet run
    /// or failed). Use <c>POST /delivery/retry-submit</c> to trigger submission.
    /// </summary>
    long? QuiqupOrderId,

    /// <summary>
    /// Quiqup parcel tracking URL surfaced to the admin portal and optionally
    /// forwarded to the customer. Null until Phase 4 completes successfully.
    /// </summary>
    string? QuiqupTrackingUrl,

    string ShippingAddress
);

