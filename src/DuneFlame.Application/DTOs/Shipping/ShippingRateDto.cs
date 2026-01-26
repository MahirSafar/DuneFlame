using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Shipping;

/// <summary>
/// Data Transfer Object for Shipping Rate information.
/// Represents the shipping cost for a specific country and currency.
/// </summary>
public class ShippingRateDto
{
    /// <summary>
    /// The currency for this shipping rate.
    /// </summary>
    public Currency Currency { get; set; }

    /// <summary>
    /// The cost of shipping in the specified currency.
    /// </summary>
    public decimal Cost { get; set; }
}
