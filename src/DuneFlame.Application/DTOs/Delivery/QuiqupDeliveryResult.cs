namespace DuneFlame.Application.DTOs.Delivery;

/// <summary>
/// Result returned from <see cref="Interfaces.IQuiqupDeliveryService.CreateOrderAsync"/>.
/// Contains only the fields relevant to our domain — the full Quiqup response shape
/// lives in the Infrastructure DTO layer and is mapped down to this slim record.
/// </summary>
public record QuiqupDeliveryResult(
    /// <summary>Quiqup's internal numeric order ID (for label/status API calls).</summary>
    long QuiqupOrderId,

    /// <summary>Quiqup's canonical UUID — stored in our database as the delivery reference.</summary>
    string QuiqupUuid,

    /// <summary>
    /// Root-level parcel tracking URL (e.g., https://track-parcel.quiqup.com/{uuid}).
    /// Stored on the Order and surfaced to the customer.
    /// </summary>
    string TrackingUrl,

    /// <summary>
    /// Destination-leg tracking URL sent to the customer via SMS by Quiqup
    /// when share_tracking = true.
    /// </summary>
    string DestinationTrackingUrl,

    /// <summary>Current Quiqup order state (e.g., "pending", "ready_for_collection").</summary>
    string State,

    /// <summary>Our internal Order ID echoed back for cross-system verification.</summary>
    string PartnerOrderId
);
