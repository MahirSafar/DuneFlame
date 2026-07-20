namespace DuneFlame.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed configuration for the Quiqup Last-Mile Delivery integration.
/// Bound from the "Quiqup" section in appsettings.json via IOptions&lt;QuiqupSettings&gt;.
/// </summary>
public class QuiqupSettings
{
    public const string SectionName = "Quiqup";

    // ── API Credentials ────────────────────────────────────────────────────────

    /// <summary>
    /// Base URL for the Quiqup API (e.g., https://api.staging.quiqup.com).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>OAuth2 client identifier issued by Quiqup.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client secret issued by Quiqup.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    // ── Warehouse / Origin Configuration ──────────────────────────────────────

    /// <summary>
    /// Display name for the DuneFlame warehouse that Quiqup couriers will see
    /// as the pickup contact on the order.
    /// </summary>
    public string WarehouseContactName { get; set; } = "DuneFlame Warehouse";

    /// <summary>
    /// UAE mobile/landline number for the warehouse pickup contact,
    /// in international format (e.g., +971XXXXXXXXX).
    /// </summary>
    public string WarehouseContactPhone { get; set; } = string.Empty;

    /// <summary>
    /// Street-level address of the DuneFlame dispatch warehouse
    /// (maps to address1 in the Quiqup origin payload).
    /// </summary>
    public string WarehouseAddress { get; set; } = string.Empty;

    /// <summary>
    /// City / emirate where the warehouse is located (e.g., "Dubai").
    /// Maps to the "town" field in the Quiqup origin payload.
    /// </summary>
    public string WarehouseTown { get; set; } = "Dubai";

    // ── Webhook Security ──────────────────────────────────────────────────────

    /// <summary>
    /// The unique token provided by Quiqup to authenticate inbound webhook POST requests.
    /// Used as the HMAC-SHA1 key to verify the <c>X-Signature</c> header on each call.
    /// </summary>
    /// <remarks>
    /// <b>Never commit this value to source control.</b>
    /// Store it in Azure Key Vault, AWS Secrets Manager, or as an environment variable
    /// (e.g., <c>Quiqup__WebhookSecret</c>) for production environments.
    /// Leave empty during local development to disable webhook validation warnings.
    /// </remarks>
    public string WebhookSecret { get; set; } = string.Empty;
}
