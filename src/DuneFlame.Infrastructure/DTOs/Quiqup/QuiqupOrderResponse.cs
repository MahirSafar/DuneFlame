using System.Text.Json;
using System.Text.Json.Serialization;


namespace DuneFlame.Infrastructure.DTOs.Quiqup;

/// <summary>
/// Root envelope returned by POST /orders on success (HTTP 200).
/// Quiqup wraps the created order object inside an "order" key.
/// </summary>
public class QuiqupOrderResponseEnvelope
{
    [JsonPropertyName("order")]
    public QuiqupOrderResponse? Order { get; set; }
}

/// <summary>
/// Represents the created Quiqup delivery order returned after a successful
/// POST /orders call. Only fields we actively use are mapped; the remainder
/// are silently ignored by System.Text.Json.
/// </summary>
public class QuiqupOrderResponse
{
    /// <summary>
    /// Raw JSON element for the Quiqup order ID.
    /// Quiqup's production API may send this as a bare integer, a quoted string,
    /// or in scientific-notation form — JsonElement absorbs all three without throwing.
    /// Use <see cref="IdLongValue"/> to get the canonical long representation.
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement Id { get; set; }

    /// <summary>
    /// Normalised <see cref="long"/> representation of <see cref="Id"/>,
    /// safe regardless of whether Quiqup serialised the value as a JSON number or string.
    /// Returns 0 if the field is absent or cannot be parsed.
    /// </summary>
    [JsonIgnore]
    public long IdLongValue => Id.ValueKind switch
    {
        JsonValueKind.Number => Id.TryGetInt64(out var n) ? n : (long)Id.GetDouble(),
        JsonValueKind.String => long.TryParse(Id.GetString(), out var s) ? s : 0L,
        _                   => 0L
    };

    /// <summary>
    /// Quiqup's UUID for this order — the canonical cross-system reference.
    /// Stored in our database alongside the tracking URL.
    /// </summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    /// <summary>
    /// Root-level tracking URL for the parcel
    /// (e.g., https://track-parcel.quiqup.com/{uuid}).
    /// </summary>
    [JsonPropertyName("tracking_url")]
    public string TrackingUrl { get; set; } = string.Empty;

    /// <summary>Current order lifecycle state (e.g., "pending", "ready_for_collection").</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>Our internal Order ID echoed back by Quiqup for cross-reference.</summary>
    [JsonPropertyName("partner_order_id")]
    public string PartnerOrderId { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("payment_mode")]
    public string PaymentMode { get; set; } = string.Empty;

    [JsonPropertyName("payment_amount")]
    public string PaymentAmount { get; set; } = string.Empty;

    [JsonPropertyName("region_name")]
    public string RegionName { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("state_updated_at")]
    public string StateUpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("item_quantity_count")]
    public int ItemQuantityCount { get; set; }

    [JsonPropertyName("destination")]
    public QuiqupResponseContactPoint? Destination { get; set; }

    [JsonPropertyName("origin")]
    public QuiqupResponseContactPoint? Origin { get; set; }

    [JsonPropertyName("items")]
    public List<QuiqupResponseItem> Items { get; set; } = [];
}

/// <summary>
/// Origin / destination contact point as returned by Quiqup in the order response.
/// Contains the enriched address, per-leg tracking URL, and zone metadata.
/// </summary>
public class QuiqupResponseContactPoint
{
    /// <summary>
    /// Raw JSON element for the contact-point ID.
    /// Quiqup's production API has been observed sending this field as a bare integer,
    /// a quoted string, or a value exceeding Int64 on some region endpoints.
    /// JsonElement handles all forms transparently — use <see cref="IdValue"/> for logging.
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement Id { get; set; }

    /// <summary>
    /// Normalised string representation of <see cref="Id"/> for logging / diagnostics.
    /// Not used for any business logic or persistence.
    /// </summary>
    [JsonIgnore]
    public string IdValue => Id.ValueKind switch
    {
        JsonValueKind.Number => Id.GetRawText(),
        JsonValueKind.String => Id.GetString() ?? string.Empty,
        _                   => string.Empty
    };

    [JsonPropertyName("contact_name")]
    public string ContactName { get; set; } = string.Empty;

    [JsonPropertyName("contact_phone")]
    public string ContactPhone { get; set; } = string.Empty;

    /// <summary>
    /// Per-leg tracking URL (destination leg is the customer-facing delivery tracker).
    /// </summary>
    [JsonPropertyName("tracking_url")]
    public string TrackingUrl { get; set; } = string.Empty;

    [JsonPropertyName("tracking_token")]
    public string TrackingToken { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Whether the contact point has been verified/checked by Quiqup's system.
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; }

    /// <summary>ISO 8601 timestamp of when the courier arrived at this stop. Null until arrival.</summary>
    [JsonPropertyName("arrived_at")]
    public string? ArrivedAt { get; set; }

    /// <summary>ISO 8601 timestamp of when the courier finished at this stop. Null until completion.</summary>
    [JsonPropertyName("finished_at")]
    public string? FinishedAt { get; set; }

    [JsonPropertyName("address")]
    public QuiqupResponseAddress? Address { get; set; }
}

/// <summary>
/// Enriched address returned by Quiqup, which may include resolved coordinates
/// and additional metadata not present in the original request.
/// </summary>
public class QuiqupResponseAddress
{
    [JsonPropertyName("address1")]
    public string Address1 { get; set; } = string.Empty;

    [JsonPropertyName("address2")]
    public string? Address2 { get; set; }

    [JsonPropertyName("town")]
    public string Town { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    /// <summary>Building name if provided (e.g., "Burj Daman"). Nullable.</summary>
    [JsonPropertyName("building_name")]
    public string? BuildingName { get; set; }

    /// <summary>Apartment or unit identifier (e.g., "1502", "s10-s110"). Nullable.</summary>
    [JsonPropertyName("apartment_number")]
    public string? ApartmentNumber { get; set; }

    [JsonPropertyName("coordinates")]
    public QuiqupCoordinates? Coordinates { get; set; }
}

/// <summary>Resolved GPS coordinates returned inside the address object.</summary>
public class QuiqupCoordinates
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lng")]
    public double Lng { get; set; }
}

/// <summary>
/// Individual parcel/item entry as returned by Quiqup in the order response,
/// including the system-assigned or provided parcel barcode.
/// </summary>
/// <remarks>
/// Quiqup returns <c>id</c> as a quoted string on POST /orders responses
/// but as a bare integer on PUT ready_for_collection responses.
/// Using <see cref="System.Text.Json.JsonElement"/> absorbs both forms;
/// use <see cref="IdValue"/> to obtain a consistent string representation.
/// </remarks>
public class QuiqupResponseItem
{
    /// <summary>
    /// Raw JSON element for the item ID — may be a string or a number depending
    /// on the Quiqup endpoint that produced this response.
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement Id { get; set; }

    /// <summary>
    /// Normalised string representation of <see cref="Id"/>,
    /// regardless of whether Quiqup sent it as a number or a quoted string.
    /// </summary>
    [JsonIgnore]
    public string IdValue => Id.ValueKind == JsonValueKind.Number
        ? Id.GetInt64().ToString()
        : Id.GetString() ?? string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("parcel_barcode")]
    public string ParcelBarcode { get; set; } = string.Empty;

    [JsonPropertyName("parcel_barcode_generated_by")]
    public string ParcelBarcodeGeneratedBy { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
