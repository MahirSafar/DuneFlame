namespace DuneFlame.Application.DTOs.Shipping;

/// <summary>
/// Request DTO for calculating shipping cost.
/// Used by frontend (Apple Pay/Google Pay Express Checkout) to get dynamic shipping prices.
/// </summary>
public class CalculateShippingRequest
{
    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g., "AE", "AZ").
    /// </summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>
    /// City name (optional, for future logistics routing).
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Currency code (USD or AED).
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Order subtotal (before shipping) used for promotion calculation.
    /// </summary>
    public decimal Subtotal { get; set; }
}
