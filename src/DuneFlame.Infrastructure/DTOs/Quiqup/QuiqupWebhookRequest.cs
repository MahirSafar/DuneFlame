using System.Text.Json.Serialization;

namespace DuneFlame.Infrastructure.DTOs.Quiqup;

/// <summary>
/// Represents the incoming webhook payload that Quiqup POSTs to our endpoint
/// whenever an order is created or updated.
/// </summary>
/// <remarks>
/// The payload is validated via HMAC-SHA1 before deserialisation.
/// See <see cref="Services.QuiqupSignatureVerifier"/> for the verification logic.
/// </remarks>
public class QuiqupWebhookRequest
{
    /// <summary>
    /// Describes the operation that triggered the notification (e.g., <c>"update"</c>).
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// The resource type that was updated (e.g., <c>"order"</c>).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The full, updated order snapshot at the time the webhook was fired.
    /// Maps directly to the same <see cref="QuiqupOrderResponse"/> structure
    /// returned by the REST order endpoints.
    /// </summary>
    [JsonPropertyName("payload")]
    public QuiqupOrderResponse Payload { get; set; } = new();

    /// <summary>
    /// UTC timestamp indicating when Quiqup dispatched this notification.
    /// </summary>
    [JsonPropertyName("sent_at")]
    public DateTime SentAt { get; set; }
}
