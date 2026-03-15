namespace DuneFlame.Application.DTOs.Shipping;

/// <summary>
/// Response DTO for shipping cost calculation.
/// Returned to frontend for Apple Pay/Google Pay Express Checkout dynamic pricing.
/// </summary>
public class CalculateShippingResponse
{
    /// <summary>
    /// Calculated shipping price in the requested currency.
    /// </summary>
    public decimal ShippingPrice { get; set; }

    /// <summary>
    /// Currency of the shipping price.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Whether shipping is available for the requested country.
    /// </summary>
    public bool Available { get; set; }

    /// <summary>
    /// Estimated delivery time (e.g., "2-3 business days").
    /// </summary>
    public string EstimatedDays { get; set; } = "2-3 business days";

    /// <summary>
    /// Status indicator for Stripe Payment Request API.
    /// "success" = shipping is available
    /// "invalid_shipping_address" = country not supported
    /// </summary>
    public string Status { get; set; } = "success";

    /// <summary>
    /// Optional message for debugging or user-facing display.
    /// </summary>
    public string Message { get; set; } = "Success";
}
