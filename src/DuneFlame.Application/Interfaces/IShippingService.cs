using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.Interfaces;

/// <summary>
/// Service interface for managing shipping-related operations.
/// Handles CRUD operations for countries, cities, and shipping rates.
/// Provides dynamic shipping cost calculation from database instead of hardcoded values.
/// </summary>
public interface IShippingService
{
    /// <summary>
    /// Gets the shipping cost for a specific country and currency.
    /// </summary>
    /// <param name="countryCode">ISO 3166-1 alpha-2 country code (e.g., "US", "AE", "CA").</param>
    /// <param name="currency">The currency for shipping cost (USD or AED).</param>
    /// <returns>The shipping cost in the specified currency, or 0 if not found.</returns>
    Task<decimal> GetShippingCostAsync(string countryCode, Currency currency);

    /// <summary>
    /// Gets the shipping cost with promotion logic applied.
    /// Applies free shipping for UAE (AE) orders with subtotal >= 200 AED or >= 55 USD.
    /// </summary>
    /// <param name="countryCode">ISO 3166-1 alpha-2 country code (e.g., "US", "AE", "CA").</param>
    /// <param name="currency">The currency for shipping cost (USD or AED).</param>
    /// <param name="subtotal">The order subtotal to check against promotion thresholds.</param>
    /// <returns>The shipping cost in the specified currency (0 if promotion applies), or regular cost otherwise.</returns>
    Task<decimal> GetShippingCostWithPromotionAsync(string countryCode, Currency currency, decimal subtotal);

    // ===== COUNTRY OPERATIONS =====

    /// <summary>
    /// Gets all countries in the system.
    /// </summary>
    /// <param name="includeInactive">Whether to include inactive countries. Default is false.</param>
    /// <returns>List of countries.</returns>
    Task<List<Country>> GetAllCountriesAsync(bool includeInactive = false);

    /// <summary>
    /// Gets a country by its unique code.
    /// </summary>
    /// <param name="code">ISO 3166-1 alpha-2 country code.</param>
    /// <returns>The country, or null if not found.</returns>
    Task<Country?> GetCountryByCodeAsync(string code);

    /// <summary>
    /// Gets a country by its ID.
    /// </summary>
    /// <param name="countryId">The country ID.</param>
    /// <returns>The country, or null if not found.</returns>
    Task<Country?> GetCountryByIdAsync(Guid countryId);

    /// <summary>
    /// Adds a new country to the system.
    /// </summary>
    /// <param name="name">Full country name.</param>
    /// <param name="code">ISO 3166-1 alpha-2 country code (must be exactly 2 characters).</param>
    /// <returns>The newly created country.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a country with the same code already exists.</exception>
    Task<Country> AddCountryAsync(string name, string code);

    /// <summary>
    /// Updates an existing country.
    /// </summary>
    /// <param name="countryId">The ID of the country to update.</param>
    /// <param name="name">New country name.</param>
    /// <param name="isActive">Whether the country is active for shipping.</param>
    /// <returns>The updated country.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if country not found.</exception>
    Task<Country> UpdateCountryAsync(Guid countryId, string name, bool isActive);

    /// <summary>
    /// Deletes a country and all its associated cities and shipping rates.
    /// </summary>
    /// <param name="countryId">The ID of the country to delete.</param>
    /// <exception cref="KeyNotFoundException">Thrown if country not found.</exception>
    Task DeleteCountryAsync(Guid countryId);

    // ===== CITY OPERATIONS =====

    /// <summary>
    /// Gets all cities for a specific country.
    /// </summary>
    /// <param name="countryId">The ID of the country.</param>
    /// <returns>List of cities in the country.</returns>
    Task<List<City>> GetCitiesByCountryAsync(Guid countryId);

    /// <summary>
    /// Gets a city by its ID.
    /// </summary>
    /// <param name="cityId">The city ID.</param>
    /// <returns>The city, or null if not found.</returns>
    Task<City?> GetCityByIdAsync(Guid cityId);

    /// <summary>
    /// Adds a new city to a country.
    /// </summary>
    /// <param name="countryId">The ID of the country.</param>
    /// <param name="name">City name.</param>
    /// <returns>The newly created city.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if country not found.</exception>
    Task<City> AddCityAsync(Guid countryId, string name);

    /// <summary>
    /// Deletes a city from the system.
    /// </summary>
    /// <param name="cityId">The ID of the city to delete.</param>
    /// <exception cref="KeyNotFoundException">Thrown if city not found.</exception>
    Task DeleteCityAsync(Guid cityId);

    // ===== SHIPPING RATE OPERATIONS =====

    /// <summary>
    /// Gets all shipping rates for a specific country.
    /// </summary>
    /// <param name="countryId">The ID of the country.</param>
    /// <returns>List of shipping rates for the country (one per currency).</returns>
    Task<List<ShippingRate>> GetShippingRatesByCountryAsync(Guid countryId);

    /// <summary>
    /// Gets a shipping rate by its ID.
    /// </summary>
    /// <param name="shippingRateId">The shipping rate ID.</param>
    /// <returns>The shipping rate, or null if not found.</returns>
    Task<ShippingRate?> GetShippingRateByIdAsync(Guid shippingRateId);

    /// <summary>
    /// Sets or updates the shipping rate for a specific country and currency.
    /// If a rate already exists for this country/currency combination, it will be updated.
    /// </summary>
    /// <param name="countryId">The ID of the country.</param>
    /// <param name="currency">The currency for the shipping rate.</param>
    /// <param name="cost">The shipping cost in the specified currency.</param>
    /// <returns>The created or updated shipping rate.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if country not found.</exception>
    /// <exception cref="ArgumentException">Thrown if cost is negative.</exception>
    Task<ShippingRate> SetShippingRateAsync(Guid countryId, Currency currency, decimal cost);

    /// <summary>
    /// Deletes a shipping rate.
    /// </summary>
    /// <param name="shippingRateId">The ID of the shipping rate to delete.</param>
    /// <exception cref="KeyNotFoundException">Thrown if shipping rate not found.</exception>
    Task DeleteShippingRateAsync(Guid shippingRateId);
}
