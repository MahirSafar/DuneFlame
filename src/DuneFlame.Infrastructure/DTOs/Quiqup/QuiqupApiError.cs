using System.Text.Json.Serialization;

namespace DuneFlame.Infrastructure.DTOs.Quiqup;

/// <summary>
/// Envelope returned by Quiqup on non-2xx responses.
/// The API always wraps its error object in an "api_error" root key.
/// </summary>
public class QuiqupApiErrorEnvelope
{
    [JsonPropertyName("api_error")]
    public QuiqupApiError? ApiError { get; set; }
}

/// <summary>
/// Structured error payload returned by the Quiqup API on failure.
/// </summary>
public class QuiqupApiError
{
    /// <summary>Internal Quiqup error identifier (e.g., 4002).</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Short machine-readable error code (e.g., "not_found").</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Brief developer-facing error message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Human-readable summary suitable for logging to support.</summary>
    [JsonPropertyName("human")]
    public string Human { get; set; } = string.Empty;

    /// <summary>Verbose human-readable explanation of the error cause.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>HTTP status code mirrored inside the body for convenience.</summary>
    [JsonPropertyName("http_status_code")]
    public int HttpStatusCode { get; set; }

    /// <summary>
    /// Per-attribute validation errors. Keys are field names; values are error codes.
    /// Only present on validation (422) responses.
    /// </summary>
    [JsonPropertyName("attribute_errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? AttributeErrors { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"[Quiqup {HttpStatusCode}] {Code}: {Human} — {Description}";
}
