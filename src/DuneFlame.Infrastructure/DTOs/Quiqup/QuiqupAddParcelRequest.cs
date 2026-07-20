using System.Text.Json.Serialization;

namespace DuneFlame.Infrastructure.DTOs.Quiqup;

/// <summary>
/// JSON request body for POST /orders/{order_id}/parcels.
/// Appends an additional physical parcel to a shipment task that is still
/// in the <c>pending</c> state.
/// </summary>
public class QuiqupAddParcelRequest
{
    /// <summary>
    /// Parcel reference name or product details tag visible on the Quiqup portal
    /// and to couriers (e.g., "Espresso Blend 500g – Order #1042 Parcel 2 of 3").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
