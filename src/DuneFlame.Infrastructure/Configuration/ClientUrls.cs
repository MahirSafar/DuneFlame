namespace DuneFlame.Infrastructure.Configuration;

/// <summary>
/// Configuration for client-facing URLs used in email links, redirects, etc.
/// </summary>
public class ClientUrls
{
    public const string SectionName = "ClientUrls";
    
    /// <summary>
    /// Base URL for the frontend application (e.g., https://duneflame.com or http://localhost:3000)
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Base URL for the API (e.g., https://api.duneflame.com or http://localhost:7190)
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;
}
