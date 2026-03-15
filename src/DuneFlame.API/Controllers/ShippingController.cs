using DuneFlame.Application.DTOs.Shipping;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Public API controller for shipping-related operations.
/// Provides endpoints for fetching countries, cities, and shipping rates for checkout.
/// No authentication required for these endpoints as they provide public reference data.
/// </summary>
[Route("api/v1/shipping")]
[ApiController]
public class ShippingController(IShippingService shippingService) : ControllerBase
{
    private readonly IShippingService _shippingService = shippingService;

    /// <summary>
    /// Gets all active countries with their shipping rates.
    /// Used by frontend to populate the country dropdown in checkout.
    /// </summary>
    /// <returns>List of countries with shipping rates grouped by currency.</returns>
    /// <response code="200">Successfully retrieved countries with shipping rates.</response>
    /// <response code="500">Internal server error.</response>
    [HttpGet("countries")]
    [ProducesResponseType(typeof(List<CountryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCountries()
    {
        try
        {
            var countries = await _shippingService.GetAllCountriesAsync(includeInactive: false);

            var countryDtos = countries.Select(c => new CountryDto
            {
                Id = c.Id,
                Name = c.Name,
                Code = c.Code,
                CityCount = c.Cities.Count,
                ShippingRates = c.ShippingRates
                    .Select(sr => new ShippingRateDto
                    {
                        Currency = sr.Currency,
                        Cost = sr.Cost
                    })
                    .ToList()
            }).ToList();

            return Ok(countryDtos);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while retrieving countries.", error = ex.Message });
        }
    }

    /// <summary>
    /// Gets all cities for a specific country.
    /// Used by frontend to populate the city dropdown after country selection.
    /// </summary>
    /// <param name="countryId">The ID of the country to retrieve cities for.</param>
    /// <returns>List of cities in the specified country.</returns>
    /// <response code="200">Successfully retrieved cities.</response>
    /// <response code="404">Country not found.</response>
    /// <response code="500">Internal server error.</response>
    [HttpGet("countries/{countryId:guid}/cities")]
    [ProducesResponseType(typeof(List<CityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCitiesByCountry(Guid countryId)
    {
        try
        {
            var country = await _shippingService.GetCountryByIdAsync(countryId);

            if (country == null)
            {
                return NotFound(new { message = $"Country with ID '{countryId}' not found." });
            }

            var cityDtos = country.Cities
                .Select(c => new CityDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    CountryId = c.CountryId
                })
                .OrderBy(c => c.Name)
                .ToList();

            return Ok(cityDtos);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while retrieving cities.", error = ex.Message });
        }
    }

    /// <summary>
    /// Gets shipping information for a specific country by country code.
    /// Alternative endpoint that accepts country code instead of ID.
    /// </summary>
    /// <param name="code">ISO 3166-1 alpha-2 country code (e.g., "US", "AE", "CA").</param>
    /// <returns>Country information with shipping rates and city count.</returns>
    /// <response code="200">Successfully retrieved country shipping information.</response>
    /// <response code="404">Country code not found.</response>
    /// <response code="500">Internal server error.</response>
    [HttpGet("countries/code/{code}")]
    [ProducesResponseType(typeof(CountryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCountryByCode(string code)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length != 2)
            {
                return BadRequest(new { message = "Country code must be exactly 2 characters (ISO 3166-1 alpha-2)." });
            }

            var country = await _shippingService.GetCountryByCodeAsync(code);

            if (country == null)
            {
                return NotFound(new { message = $"Country with code '{code}' not found." });
            }

            var countryDto = new CountryDto
            {
                Id = country.Id,
                Name = country.Name,
                Code = country.Code,
                CityCount = country.Cities.Count,
                ShippingRates = country.ShippingRates
                    .Select(sr => new ShippingRateDto
                    {
                        Currency = sr.Currency,
                        Cost = sr.Cost
                    })
                    .ToList()
            };

            return Ok(countryDto);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while retrieving country information.", error = ex.Message });
        }
    }

    /// <summary>
    /// Calculates dynamic shipping cost for Express Checkout (Apple Pay/Google Pay).
    /// Used by frontend to update shipping price when user selects a shipping address.
    /// Applies promotion logic (free shipping for high-value orders).
    /// </summary>
    /// <param name="request">Shipping calculation request containing country code, currency, and subtotal.</param>
    /// <returns>Calculated shipping price and availability status.</returns>
    /// <response code="200">Successfully calculated shipping cost.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="500">Internal server error.</response>
    [HttpPost("calculate")]
    [ProducesResponseType(typeof(CalculateShippingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CalculateShipping([FromBody] CalculateShippingRequest request)
    {
        try
        {
            // Validate request
            if (request == null || string.IsNullOrWhiteSpace(request.CountryCode) || string.IsNullOrWhiteSpace(request.Currency))
            {
                return BadRequest(new { message = "CountryCode and Currency are required." });
            }

            if (request.Subtotal < 0)
            {
                return BadRequest(new { message = "Subtotal cannot be negative." });
            }

            // Normalize country code to 2 characters (following copilot instructions)
            string normalizedCountryCode = request.CountryCode.Length > 2 
                ? request.CountryCode.Substring(0, 2) 
                : request.CountryCode;

            // Validate and parse currency
            if (!Enum.TryParse<DuneFlame.Domain.Enums.Currency>(request.Currency, ignoreCase: true, out var currency))
            {
                return BadRequest(new { message = "Invalid currency. Supported currencies: USD, AED." });
            }

            // Check if country exists and is active
            var country = await _shippingService.GetCountryByCodeAsync(normalizedCountryCode);
            if (country == null || !country.IsActive)
            {
                return Ok(new CalculateShippingResponse
                {
                    ShippingPrice = 0,
                    Currency = request.Currency,
                    Available = false,
                    Status = "invalid_shipping_address",
                    Message = $"Shipping is not available to {normalizedCountryCode}."
                });
            }

            // Calculate shipping cost with promotion logic
            var shippingPrice = await _shippingService.GetShippingCostWithPromotionAsync(
                normalizedCountryCode,
                currency,
                request.Subtotal);

            return Ok(new CalculateShippingResponse
            {
                ShippingPrice = shippingPrice,
                Currency = request.Currency,
                Available = true,
                Status = "success",
                Message = "Shipping cost calculated successfully."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while calculating shipping cost.", error = ex.Message });
        }
    }
}
