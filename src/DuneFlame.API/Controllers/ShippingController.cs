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
}
