using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents the shipping cost for a specific country and currency.
/// Enables currency-aware and country-specific shipping calculations.
/// </summary>
public class ShippingRate : BaseEntity
{
    /// <summary>
    /// Foreign key to the Country this shipping rate applies to.
    /// </summary>
    public Guid CountryId { get; set; }

    /// <summary>
    /// The currency for this shipping rate (USD or AED).
    /// </summary>
    [Required]
    public Currency Currency { get; set; } = Currency.USD;

    /// <summary>
    /// The cost of shipping to this country in the specified currency.
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal Cost { get; set; }

    // Navigation property
    public Country? Country { get; set; }
}
