namespace DuneFlame.Application.DTOs.Shipping;

/// <summary>
/// Data Transfer Object for Country information.
/// Used for returning country data and their shipping rates to the frontend.
/// </summary>
public class CountryDto
{
    /// <summary>
    /// Unique identifier of the country.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Full name of the country (e.g., "United States", "United Arab Emirates").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g., "US", "AE", "CA", "GB").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Shipping rates for this country in different currencies.
    /// </summary>
    public List<ShippingRateDto> ShippingRates { get; set; } = [];

    /// <summary>
    /// Number of cities available for this country.
    /// Can be used by frontend to know if city selection is applicable.
    /// </summary>
    public int CityCount { get; set; }
}
