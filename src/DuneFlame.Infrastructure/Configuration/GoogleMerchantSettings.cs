namespace DuneFlame.Infrastructure.Configuration;

/// <summary>
/// Configuration for Google Merchant Center integration.
/// Bound from the "GoogleMerchant" section in appsettings.json.
///
/// Authentication is handled entirely by Application Default Credentials (ADC).
/// No key files or secrets are needed here.
/// </summary>
public class GoogleMerchantSettings
{
    public const string SectionName = "GoogleMerchant";

    /// <summary>
    /// Your Google Merchant Center numeric account ID (e.g. 123456789).
    /// </summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>
    /// Target country code for all products (ISO 3166-1 alpha-2, e.g. "AE" for UAE).
    /// </summary>
    public string TargetCountry { get; set; } = "AE";

    /// <summary>
    /// Content language for all products (ISO 639-1, e.g. "en").
    /// </summary>
    public string ContentLanguage { get; set; } = "en";

    /// <summary>
    /// Canonical domain used to build product page links (e.g. "https://duneflame.com").
    /// </summary>
    public string StorefrontBaseUrl { get; set; } = "https://duneflame.com";

    /// <summary>
    /// Set to false to disable Merchant Center sync without removing the service.
    /// Useful for local development or staging environments.
    /// </summary>
    public bool Enabled { get; set; } = false;
}
