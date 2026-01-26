using DuneFlame.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Calculates shipping costs based on destination country and currency.
/// Shipping rates are currency-aware to reflect regional pricing.
/// </summary>
public class ShippingCalculator
{
    private readonly ILogger _logger;

    public ShippingCalculator(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculate shipping cost for a given destination country and currency.
    /// </summary>
    /// <param name="country">Destination country (e.g., "United States", "United Arab Emirates")</param>
    /// <param name="currency">Currency enum (USD or AED)</param>
    /// <returns>Shipping cost in the specified currency</returns>
    public decimal CalculateShippingCost(string country, Currency currency)
    {
        // Normalize country name for consistent lookup
        var normalizedCountry = country?.Trim().ToLower() ?? string.Empty;

        // Define shipping rates by country and currency
        var shippingRates = new Dictionary<(string, Currency), decimal>
        {
            { ("united states", Currency.USD), 10.00m },
            { ("usa", Currency.USD), 10.00m },
            { ("us", Currency.USD), 10.00m },
            { ("united arab emirates", Currency.AED), 36.70m },
            { ("uae", Currency.AED), 36.70m },
            { ("canada", Currency.USD), 15.00m },
            { ("canada", Currency.AED), 55.05m },
            { ("united kingdom", Currency.USD), 20.00m },
            { ("uk", Currency.USD), 20.00m },
            { ("united kingdom", Currency.AED), 73.40m },
            { ("uk", Currency.AED), 73.40m },
            { ("australia", Currency.USD), 25.00m },
            { ("australia", Currency.AED), 91.75m },
            { ("default", Currency.USD), 30.00m },
            { ("default", Currency.AED), 110.10m },
        };

        // Try exact match first
        if (shippingRates.TryGetValue((normalizedCountry, currency), out var exactRate))
        {
            _logger.LogInformation("Shipping cost calculated for {Country}/{Currency}: {Cost}",
                country, currency, exactRate);
            return exactRate;
        }

        // Fall back to default rate for currency
        var defaultKey = ("default", currency);
        if (shippingRates.TryGetValue(defaultKey, out var defaultRate))
        {
            _logger.LogWarning("Country '{Country}' not found in shipping table. Using default rate for {Currency}: {Cost}",
                country, currency, defaultRate);
            return defaultRate;
        }

        // Absolute fallback (should rarely happen)
        var fallbackRate = currency == Currency.USD ? 30.00m : 110.10m;
        _logger.LogWarning("No shipping rate found for {Country}/{Currency}. Using fallback rate: {Cost}",
            country, currency, fallbackRate);
        return fallbackRate;
    }

    /// <summary>
    /// Calculate total price including shipping.
    /// </summary>
    /// <param name="subtotal">Subtotal before shipping</param>
    /// <param name="country">Destination country</param>
    /// <param name="currency">Currency of the order</param>
    /// <returns>Total price = Subtotal + Shipping</returns>
    public decimal CalculateTotalWithShipping(decimal subtotal, string country, Currency currency)
    {
        var shippingCost = CalculateShippingCost(country, currency);
        var total = subtotal + shippingCost;

        _logger.LogInformation(
            "Order total calculated: Subtotal={Subtotal}, Shipping={Shipping}, Total={Total} {Currency}",
            subtotal, shippingCost, total, currency);

        return total;
    }
}
