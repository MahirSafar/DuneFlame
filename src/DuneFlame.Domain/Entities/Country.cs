using DuneFlame.Domain.Common;
using System.ComponentModel.DataAnnotations;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a country in the shipping system.
/// Stores country information for shipping rate management and address validation.
/// </summary>
public class Country : BaseEntity
{
    /// <summary>
    /// Full name of the country (e.g., "United States", "United Arab Emirates").
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g., "US", "AE", "CA", "GB").
    /// Must be unique across all countries.
    /// </summary>
    [Required]
    [StringLength(2, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Whether this country is active for shipping.
    /// Inactive countries are excluded from shipping options.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<City> Cities { get; set; } = [];
    public ICollection<ShippingRate> ShippingRates { get; set; } = [];
}
