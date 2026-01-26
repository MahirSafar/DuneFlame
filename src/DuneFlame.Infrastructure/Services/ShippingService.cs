using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Service for managing shipping-related data and calculations.
/// Provides database-backed shipping cost lookups and CRUD operations for countries, cities, and rates.
/// Replaces the hardcoded ShippingCalculator dictionary with a scalable, admin-managed system.
/// </summary>
public class ShippingService(
    AppDbContext context,
    ILogger<ShippingService> logger) : IShippingService
{
    public async Task<decimal> GetShippingCostAsync(string countryCode, Currency currency)
    {
        try
        {
            // First, try to find the exact rate for the country and currency
            var shippingRate = await context.ShippingRates
                .Include(sr => sr.Country)
                .FirstOrDefaultAsync(sr =>
                    sr.Country != null &&
                    sr.Country.Code.ToUpper() == countryCode.ToUpper() &&
                    sr.Currency == currency &&
                    sr.Country.IsActive);

            if (shippingRate != null)
            {
                logger.LogInformation(
                    "Shipping cost found for country '{CountryCode}' in {Currency}: {Cost}",
                    countryCode, currency, shippingRate.Cost);
                return shippingRate.Cost;
            }

            // If not found, check if the country exists (to determine if it's serviced)
            var country = await context.Countries
                .FirstOrDefaultAsync(c => c.Code.ToUpper() == countryCode.ToUpper() && c.IsActive);

            if (country == null)
            {
                logger.LogWarning(
                    "Country '{CountryCode}' is not serviced. Returning 0.",
                    countryCode);
                return 0m;
            }

            // Country exists but rate for requested currency not found
            // SMART FALLBACK (Bidirectional): Try to find ANY rate for this country and convert it
            const decimal AedToUsdRate = 3.67m; // 1 AED = 3.67 USD

            // Try to find USD rate first
            var usdRate = await context.ShippingRates
                .FirstOrDefaultAsync(sr =>
                    sr.CountryId == country.Id &&
                    sr.Currency == Currency.USD);

            if (usdRate != null)
            {
                decimal convertedRate = ConvertCurrency(usdRate.Cost, Currency.USD, currency, AedToUsdRate);
                logger.LogInformation(
                    "Shipping rate fallback for country '{CountryCode}': Converted from USD {UsdRate} to {Currency} {ConvertedRate}",
                    countryCode, usdRate.Cost, currency, convertedRate);
                return convertedRate;
            }

            // If USD not found, try to find AED rate
            var aedRate = await context.ShippingRates
                .FirstOrDefaultAsync(sr =>
                    sr.CountryId == country.Id &&
                    sr.Currency == Currency.AED);

            if (aedRate != null)
            {
                decimal convertedRate = ConvertCurrency(aedRate.Cost, Currency.AED, currency, AedToUsdRate);
                logger.LogInformation(
                    "Shipping rate fallback for country '{CountryCode}': Converted from AED {AedRate} to {Currency} {ConvertedRate}",
                    countryCode, aedRate.Cost, currency, convertedRate);
                return convertedRate;
            }

            // No fallback available - try any other currency for the country
            var anyRate = await context.ShippingRates
                .FirstOrDefaultAsync(sr => sr.CountryId == country.Id);

            if (anyRate != null)
            {
                decimal convertedRate = ConvertCurrency(anyRate.Cost, anyRate.Currency, currency, AedToUsdRate);
                logger.LogInformation(
                    "Shipping rate fallback for country '{CountryCode}': Converted from {FromCurrency} {OriginalRate} to {ToCurrency} {ConvertedRate}",
                    countryCode, anyRate.Currency, anyRate.Cost, currency, convertedRate);
                return convertedRate;
            }

            // No rates available at all for this country
            logger.LogWarning(
                "Shipping rate not found for country '{CountryCode}' and currency '{Currency}', and no fallback rates available. Returning default cost of 0.",
                countryCode, currency);
            return 0m;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error retrieving shipping cost for country '{CountryCode}' and currency '{Currency}'",
                countryCode, currency);
            throw;
        }
    }

    /// <summary>
    /// Converts a rate from one currency to another using the AED/USD conversion rate.
    /// </summary>
    private static decimal ConvertCurrency(decimal amount, Currency fromCurrency, Currency toCurrency, decimal aedToUsdRate)
    {
        if (fromCurrency == toCurrency)
            return amount;

        // Convert to USD as intermediate currency
        decimal amountInUsd = fromCurrency switch
        {
            Currency.AED => amount / aedToUsdRate,
            Currency.USD => amount,
            _ => amount  // For unknown currencies, assume USD
        };

        // Convert from USD to target currency
        return toCurrency switch
        {
            Currency.AED => amountInUsd * aedToUsdRate,
            Currency.USD => amountInUsd,
            _ => amountInUsd  // For unknown currencies, return as is
        };
    }

    public async Task<List<Country>> GetAllCountriesAsync(bool includeInactive = false)
    {
        try
        {
            var query = context.Countries
                .Include(c => c.Cities)
                .Include(c => c.ShippingRates)
                .AsSplitQuery()
                .AsQueryable();

            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            var countries = await query
                .OrderBy(c => c.Name)
                .ToListAsync();

            logger.LogInformation(
                "Retrieved {CountryCount} countries (includeInactive: {IncludeInactive})",
                countries.Count, includeInactive);

            return countries;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving countries");
            throw;
        }
    }

    public async Task<Country?> GetCountryByCodeAsync(string code)
    {
        try
        {
            var country = await context.Countries
                .Include(c => c.Cities)
                .Include(c => c.ShippingRates)
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Code.ToUpper() == code.ToUpper());

            if (country != null)
            {
                logger.LogInformation("Country found with code '{Code}'", code);
            }

            return country;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving country by code '{Code}'", code);
            throw;
        }
    }

    public async Task<Country?> GetCountryByIdAsync(Guid countryId)
    {
        try
        {
            var country = await context.Countries
                .Include(c => c.Cities)
                .Include(c => c.ShippingRates)
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Id == countryId);

            if (country != null)
            {
                logger.LogInformation("Country found with ID '{CountryId}'", countryId);
            }

            return country;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving country by ID '{CountryId}'", countryId);
            throw;
        }
    }

    public async Task<Country> AddCountryAsync(string name, string code)
    {
        try
        {
            // Validate code length
            if (code.Length != 2)
            {
                throw new ArgumentException("Country code must be exactly 2 characters (ISO 3166-1 alpha-2)", nameof(code));
            }

            // Check for duplicate code
            var existingCountry = await context.Countries
                .FirstOrDefaultAsync(c => c.Code.ToUpper() == code.ToUpper());

            if (existingCountry != null)
            {
                throw new InvalidOperationException($"A country with code '{code}' already exists.");
            }

            var country = new Country
            {
                Id = Guid.NewGuid(),
                Name = name,
                Code = code.ToUpper(),
                IsActive = true
            };

            context.Countries.Add(country);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "Country added: Id='{CountryId}', Name='{Name}', Code='{Code}'",
                country.Id, country.Name, country.Code);

            return country;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding country with code '{Code}'", code);
            throw;
        }
    }

    public async Task<Country> UpdateCountryAsync(Guid countryId, string name, bool isActive)
    {
        try
        {
            var country = await context.Countries.FirstOrDefaultAsync(c => c.Id == countryId);

            if (country == null)
            {
                throw new KeyNotFoundException($"Country with ID '{countryId}' not found.");
            }

            country.Name = name;
            country.IsActive = isActive;

            context.Countries.Update(country);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "Country updated: Id='{CountryId}', Name='{Name}', IsActive='{IsActive}'",
                country.Id, country.Name, country.IsActive);

            return country;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating country with ID '{CountryId}'", countryId);
            throw;
        }
    }

    public async Task DeleteCountryAsync(Guid countryId)
    {
        try
        {
            var country = await context.Countries.FirstOrDefaultAsync(c => c.Id == countryId);

            if (country == null)
            {
                throw new KeyNotFoundException($"Country with ID '{countryId}' not found.");
            }

            context.Countries.Remove(country);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "Country deleted: Id='{CountryId}', Code='{Code}'",
                countryId, country.Code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting country with ID '{CountryId}'", countryId);
            throw;
        }
    }

    public async Task<List<City>> GetCitiesByCountryAsync(Guid countryId)
    {
        try
        {
            var cities = await context.Cities
                .Where(c => c.CountryId == countryId)
                .OrderBy(c => c.Name)
                .ToListAsync();

            logger.LogInformation(
                "Retrieved {CityCount} cities for country '{CountryId}'",
                cities.Count, countryId);

            return cities;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving cities for country '{CountryId}'", countryId);
            throw;
        }
    }

    public async Task<City?> GetCityByIdAsync(Guid cityId)
    {
        try
        {
            var city = await context.Cities
                .Include(c => c.Country)
                .FirstOrDefaultAsync(c => c.Id == cityId);

            if (city != null)
            {
                logger.LogInformation("City found with ID '{CityId}'", cityId);
            }

            return city;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving city by ID '{CityId}'", cityId);
            throw;
        }
    }

    public async Task<City> AddCityAsync(Guid countryId, string name)
    {
        try
        {
            var country = await context.Countries.FirstOrDefaultAsync(c => c.Id == countryId);

            if (country == null)
            {
                throw new KeyNotFoundException($"Country with ID '{countryId}' not found.");
            }

            var city = new City
            {
                Id = Guid.NewGuid(),
                Name = name,
                CountryId = countryId
            };

            context.Cities.Add(city);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "City added: Id='{CityId}', Name='{Name}', CountryId='{CountryId}'",
                city.Id, city.Name, city.CountryId);

            return city;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding city to country '{CountryId}'", countryId);
            throw;
        }
    }

    public async Task DeleteCityAsync(Guid cityId)
    {
        try
        {
            var city = await context.Cities.FirstOrDefaultAsync(c => c.Id == cityId);

            if (city == null)
            {
                throw new KeyNotFoundException($"City with ID '{cityId}' not found.");
            }

            context.Cities.Remove(city);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "City deleted: Id='{CityId}', Name='{Name}'",
                cityId, city.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting city with ID '{CityId}'", cityId);
            throw;
        }
    }

    public async Task<List<ShippingRate>> GetShippingRatesByCountryAsync(Guid countryId)
    {
        try
        {
            var rates = await context.ShippingRates
                .Where(sr => sr.CountryId == countryId)
                .OrderBy(sr => sr.Currency)
                .ToListAsync();

            logger.LogInformation(
                "Retrieved {RateCount} shipping rates for country '{CountryId}'",
                rates.Count, countryId);

            return rates;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving shipping rates for country '{CountryId}'", countryId);
            throw;
        }
    }

    public async Task<ShippingRate?> GetShippingRateByIdAsync(Guid shippingRateId)
    {
        try
        {
            var rate = await context.ShippingRates
                .Include(sr => sr.Country)
                .FirstOrDefaultAsync(sr => sr.Id == shippingRateId);

            if (rate != null)
            {
                logger.LogInformation("Shipping rate found with ID '{ShippingRateId}'", shippingRateId);
            }

            return rate;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving shipping rate by ID '{ShippingRateId}'", shippingRateId);
            throw;
        }
    }

    public async Task<ShippingRate> SetShippingRateAsync(Guid countryId, Currency currency, decimal cost)
    {
        try
        {
            if (cost < 0)
            {
                throw new ArgumentException("Shipping cost cannot be negative", nameof(cost));
            }

            var country = await context.Countries.FirstOrDefaultAsync(c => c.Id == countryId);

            if (country == null)
            {
                throw new KeyNotFoundException($"Country with ID '{countryId}' not found.");
            }

            // Check if rate already exists
            var existingRate = await context.ShippingRates
                .FirstOrDefaultAsync(sr => sr.CountryId == countryId && sr.Currency == currency);

            if (existingRate != null)
            {
                // Update existing rate
                existingRate.Cost = cost;
                context.ShippingRates.Update(existingRate);

                logger.LogInformation(
                    "Shipping rate updated: Id='{RateId}', Country='{CountryId}', Currency='{Currency}', Cost='{Cost}'",
                    existingRate.Id, countryId, currency, cost);

                await context.SaveChangesAsync();
                return existingRate;
            }

            // Create new rate
            var shippingRate = new ShippingRate
            {
                Id = Guid.NewGuid(),
                CountryId = countryId,
                Currency = currency,
                Cost = cost
            };

            context.ShippingRates.Add(shippingRate);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "Shipping rate created: Id='{RateId}', Country='{CountryId}', Currency='{Currency}', Cost='{Cost}'",
                shippingRate.Id, countryId, currency, cost);

            return shippingRate;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error setting shipping rate for country '{CountryId}' and currency '{Currency}'",
                countryId, currency);
            throw;
        }
    }

    public async Task DeleteShippingRateAsync(Guid shippingRateId)
    {
        try
        {
            var rate = await context.ShippingRates.FirstOrDefaultAsync(sr => sr.Id == shippingRateId);

            if (rate == null)
            {
                throw new KeyNotFoundException($"Shipping rate with ID '{shippingRateId}' not found.");
            }

            context.ShippingRates.Remove(rate);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "Shipping rate deleted: Id='{RateId}', Currency='{Currency}'",
                shippingRateId, rate.Currency);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting shipping rate with ID '{ShippingRateId}'", shippingRateId);
            throw;
        }
    }
}
