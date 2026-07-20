using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DuneFlame.Infrastructure.DTOs.Quiqup;

/// <summary>
/// JSON request body for PUT /orders/batch/set_cancelled.
/// Wraps one or more numeric Quiqup order IDs to cancel in a single call.
/// </summary>
/// <remarks>
/// Although the API supports true batch cancellation, the application layer exposes
/// a single-order workflow via <c>CancelOrderAsync</c> for simplicity.
/// The <see cref="OrderIds"/> list therefore always contains exactly one element.
/// </remarks>
public class QuiqupCancelOrdersRequest
{
    /// <summary>
    /// Array of Quiqup internal numeric order IDs to cancel.
    /// </summary>
    [JsonPropertyName("order_ids")]
    public List<long> OrderIds { get; set; } = new();
}
