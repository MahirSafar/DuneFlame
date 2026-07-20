using System.Text.Json.Serialization;

namespace DuneFlame.Infrastructure.DTOs.Quiqup;

/// <summary>
/// Root envelope returned by GET /orders on success (HTTP 200).
/// Quiqup returns the paginated order list wrapped under an "orders" key,
/// consistent with the single-order "order" key used by POST /orders.
/// </summary>
public class QuiqupListOrdersEnvelope
{
    [JsonPropertyName("orders")]
    public List<QuiqupOrderResponse> Orders { get; set; } = [];

    /// <summary>
    /// Optional pagination metadata returned alongside the order list.
    /// May be absent on some API versions — always check for null.
    /// </summary>
    [JsonPropertyName("meta")]
    public QuiqupListMeta? Meta { get; set; }
}

/// <summary>
/// Pagination metadata accompanying a GET /orders list response.
/// </summary>
public class QuiqupListMeta
{
    /// <summary>Total number of orders matching the filter (across all pages).</summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    /// <summary>Current page number (1-indexed).</summary>
    [JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }

    /// <summary>Maximum number of orders per page as requested.</summary>
    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }

    /// <summary>Total number of pages available.</summary>
    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}
