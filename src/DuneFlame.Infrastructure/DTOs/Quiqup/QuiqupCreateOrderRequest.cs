using System.Text.Json.Serialization;

namespace DuneFlame.Infrastructure.DTOs.Quiqup;

/// <summary>
/// Root payload sent to POST /orders on the Quiqup Ecommerce API.
/// </summary>
public class QuiqupCreateOrderRequest
{
    /// <summary>
    /// Delivery product kind. Use "partner_next_day" for standard next-day delivery.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "partner_next_day";

    /// <summary>
    /// Payment mode sent to Quiqup.
    /// "pre_paid" — customer already paid online (Stripe); Quiqup does not collect.
    /// "paid_on_delivery" — courier collects payment at the door (COD orders).
    /// Set dynamically from <see cref="DuneFlame.Domain.Enums.PaymentMethod"/>.
    /// </summary>
    [JsonPropertyName("payment_mode")]
    public string PaymentMode { get; set; } = "pre_paid";

    /// <summary>
    /// Payment amount in AED (whole integer). Must be 0 for pre_paid orders.
    /// For paid_on_delivery orders this is the order total rounded to the nearest dirham,
    /// so the courier knows the exact amount to collect at the door.
    /// </summary>
    [JsonPropertyName("payment_amount")]
    public double PaymentAmount { get; set; } = 0;

    /// <summary>
    /// Our internal Order ID (GUID) used as the partner reference for cross-system traceability.
    /// </summary>
    [JsonPropertyName("partner_order_id")]
    public string PartnerOrderId { get; set; } = string.Empty;

    [JsonPropertyName("origin")]
    public QuiqupContactPoint Origin { get; set; } = new();

    [JsonPropertyName("destination")]
    public QuiqupDestination Destination { get; set; } = new();

    [JsonPropertyName("items")]
    public List<QuiqupParcelItem> Items { get; set; } = [];
}

/// <summary>
/// Contact point used for the pickup (origin) side of the delivery.
/// </summary>
public class QuiqupContactPoint
{
    [JsonPropertyName("contact_name")]
    public string ContactName { get; set; } = string.Empty;

    [JsonPropertyName("contact_phone")]
    public string ContactPhone { get; set; } = string.Empty;

    /// <summary>
    /// Free-text courier instructions (e.g. "Go to the loading bay on the ground floor").
    /// </summary>
    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }

    [JsonPropertyName("address")]
    public QuiqupAddress Address { get; set; } = new();
}

/// <summary>
/// Destination (drop-off) contact point with extra tracking-share capability.
/// </summary>
public class QuiqupDestination
{
    [JsonPropertyName("contact_name")]
    public string ContactName { get; set; } = string.Empty;

    [JsonPropertyName("contact_phone")]
    public string ContactPhone { get; set; } = string.Empty;

    /// <summary>
    /// When true, Quiqup sends a live-tracking SMS/link to the recipient.
    /// Always set to true for customer-facing deliveries.
    /// </summary>
    [JsonPropertyName("share_tracking")]
    public bool ShareTracking { get; set; } = true;

    /// <summary>
    /// Free-text delivery instructions visible to the courier
    /// (e.g. "Ring bell — apartment on the 3rd floor").
    /// </summary>
    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }

    [JsonPropertyName("address")]
    public QuiqupAddress Address { get; set; } = new();
}

/// <summary>
/// Physical address sub-object shared by origin and destination.
/// </summary>
public class QuiqupAddress
{
    /// <summary>Primary street / building line. Required by Quiqup.</summary>
    [JsonPropertyName("address1")]
    public string Address1 { get; set; } = string.Empty;

    /// <summary>
    /// Secondary address line — apartment number, floor, building name, etc.
    /// Omitted from the serialized payload when null.
    /// </summary>
    [JsonPropertyName("address2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Address2 { get; set; }

    [JsonPropertyName("town")]
    public string Town { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = "UAE";

    /// <summary>
    /// Optional GPS coordinates [longitude, latitude] for pinpoint accuracy.
    /// Omitted from the serialized payload when null.
    /// </summary>
    [JsonPropertyName("coords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? Coords { get; set; }
}

/// <summary>
/// Represents a single parcel line item within the Quiqup order.
/// Quiqup maps each item entry to a physical parcel, so quantity is always 1.
/// To ship N physical parcels, create N separate QuiqupParcelItem entries.
/// </summary>
public class QuiqupParcelItem
{
    /// <summary>
    /// Parcel reference label visible on the Quiqup portal and to couriers.
    /// Use the product name, an order reference, or "Parcel 1 of N" style naming.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Always 1 per Quiqup's item-to-parcel mapping rule.</summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Physical weight of this parcel in kilograms.
    /// Calculated dynamically from order item quantity × per-unit weight.
    /// Minimum 0.250 kg (250 g) — Quiqup rejects zero-weight parcels.
    /// Always serialized (never omitted) so Quiqup can display accurate AWB weight.
    /// </summary>
    [JsonPropertyName("weight_kg")]
    public double WeightKg { get; set; }

    /// <summary>
    /// Optional custom barcode to use on the AWB label.
    /// If omitted, Quiqup generates a barcode automatically — required for Step 3 label download.
    /// Leave null to let Quiqup manage barcodes (recommended for initial integration).
    /// </summary>
    [JsonPropertyName("parcel_barcode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParcelBarcode { get; set; }
}
