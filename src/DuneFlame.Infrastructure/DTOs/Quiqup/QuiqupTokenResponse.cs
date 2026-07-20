using System.Text.Json.Serialization;

namespace DuneFlame.Infrastructure.DTOs.Quiqup;

/// <summary>
/// Represents the OAuth2 token response payload returned by the Quiqup
/// /oauth/token endpoint using the client_credentials grant flow.
/// </summary>
public class QuiqupTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    /// <summary>
    /// Lifetime of the token in seconds. On staging this is typically 3600 (1 hour).
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>
    /// Unix timestamp (UTC) at which the token was created, as returned by Quiqup.
    /// </summary>
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }
}
