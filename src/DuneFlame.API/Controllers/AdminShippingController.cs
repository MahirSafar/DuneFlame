using DuneFlame.Application.DTOs;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Admin API endpoints for managing shipping-related data.
/// Provides CRUD operations for countries, cities, and shipping rates.
/// Requires admin authorization for all operations.
/// </summary>
[ApiController]
[Route("api/v1/admin/shipping")]
[Authorize(Roles = "Admin")]
public class AdminShippingController(IShippingService shippingService) : ControllerBase
{
    // ===== COUNTRY ENDPOINTS =====

    /// <summary>
    /// Gets all countries.
    /// </summary>
    /// <param name="includeInactive">Whether to include inactive countries. Default is false.</param>
    [HttpGet("countries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCountries([FromQuery] bool includeInactive = false)
    {
        try
        {
            var countries = await shippingService.GetAllCountriesAsync(includeInactive);
            return Ok(new
            {
                success = true,
                data = countries
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error retrieving countries",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Gets a country by its code.
    /// </summary>
    /// <param name="code">ISO 3166-1 alpha-2 country code (e.g., "US", "AE", "CA").</param>
    [HttpGet("countries/{code}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCountryByCode(string code)
    {
        try
        {
            var country = await shippingService.GetCountryByCodeAsync(code);

            if (country == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = $"Country with code '{code}' not found"
                });
            }

            return Ok(new
            {
                success = true,
                data = country
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error retrieving country",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Adds a new country.
    /// </summary>
    /// <param name="request">Country creation request with name and code.</param>
    [HttpPost("countries")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddCountry([FromBody] AddCountryRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Country name and code are required"
                });
            }

            var country = await shippingService.AddCountryAsync(request.Name, request.Code);

            return CreatedAtAction(nameof(GetCountryByCode), new { code = country.Code }, new
            {
                success = true,
                data = country
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error creating country",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Updates an existing country.
    /// </summary>
    /// <param name="countryId">The ID of the country to update.</param>
    /// <param name="request">Country update request with name and isActive status.</param>
    [HttpPut("countries/{countryId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCountry(Guid countryId, [FromBody] UpdateCountryRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Country name is required"
                });
            }

            var country = await shippingService.UpdateCountryAsync(countryId, request.Name, request.IsActive);

            return Ok(new
            {
                success = true,
                data = country
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error updating country",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Deletes a country and all its associated cities and shipping rates.
    /// </summary>
    /// <param name="countryId">The ID of the country to delete.</param>
    [HttpDelete("countries/{countryId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCountry(Guid countryId)
    {
        try
        {
            await shippingService.DeleteCountryAsync(countryId);

            return Ok(new
            {
                success = true,
                message = "Country deleted successfully"
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error deleting country",
                error = ex.Message
            });
        }
    }

    // ===== CITY ENDPOINTS =====

    /// <summary>
    /// Gets all cities for a specific country.
    /// </summary>
    /// <param name="countryId">The ID of the country.</param>
    [HttpGet("countries/{countryId}/cities")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCitiesByCountry(Guid countryId)
    {
        try
        {
            var country = await shippingService.GetCountryByIdAsync(countryId);

            if (country == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = $"Country with ID '{countryId}' not found"
                });
            }

            var cities = await shippingService.GetCitiesByCountryAsync(countryId);

            return Ok(new
            {
                success = true,
                data = cities
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error retrieving cities",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Adds a new city to a country.
    /// </summary>
    /// <param name="countryId">The ID of the country.</param>
    /// <param name="request">City creation request with name.</param>
    [HttpPost("countries/{countryId}/cities")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddCity(Guid countryId, [FromBody] AddCityRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "City name is required"
                });
            }

            var city = await shippingService.AddCityAsync(countryId, request.Name);

            return CreatedAtAction(nameof(GetCitiesByCountry), new { countryId }, new
            {
                success = true,
                data = city
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error creating city",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Deletes a city.
    /// </summary>
    /// <param name="cityId">The ID of the city to delete.</param>
    [HttpDelete("cities/{cityId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCity(Guid cityId)
    {
        try
        {
            await shippingService.DeleteCityAsync(cityId);

            return Ok(new
            {
                success = true,
                message = "City deleted successfully"
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error deleting city",
                error = ex.Message
            });
        }
    }

    // ===== SHIPPING RATE ENDPOINTS =====

    /// <summary>
    /// Gets all shipping rates for a specific country.
    /// </summary>
    /// <param name="countryId">The ID of the country.</param>
    [HttpGet("countries/{countryId}/rates")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShippingRates(Guid countryId)
    {
        try
        {
            var country = await shippingService.GetCountryByIdAsync(countryId);

            if (country == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = $"Country with ID '{countryId}' not found"
                });
            }

            var rates = await shippingService.GetShippingRatesByCountryAsync(countryId);

            return Ok(new
            {
                success = true,
                data = rates
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error retrieving shipping rates",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Sets or updates a shipping rate for a country and currency.
    /// </summary>
    /// <param name="countryId">The ID of the country.</param>
    /// <param name="request">Shipping rate request with currency and cost.</param>
    [HttpPost("countries/{countryId}/rates")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetShippingRate(Guid countryId, [FromBody] SetShippingRateRequest request)
    {
        try
        {
            if (request.Cost < 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Shipping cost cannot be negative"
                });
            }

            var rate = await shippingService.SetShippingRateAsync(countryId, request.Currency, request.Cost);

            return CreatedAtAction(nameof(GetShippingRates), new { countryId }, new
            {
                success = true,
                data = rate,
                message = "Shipping rate set successfully"
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error setting shipping rate",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Deletes a shipping rate.
    /// </summary>
    /// <param name="rateId">The ID of the shipping rate to delete.</param>
    [HttpDelete("rates/{rateId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteShippingRate(Guid rateId)
    {
        try
        {
            await shippingService.DeleteShippingRateAsync(rateId);

            return Ok(new
            {
                success = true,
                message = "Shipping rate deleted successfully"
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Error deleting shipping rate",
                error = ex.Message
            });
        }
    }
}

// ===== DTO REQUEST CLASSES =====

/// <summary>
/// Request to add a new country.
/// </summary>
public class AddCountryRequest
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Request to update an existing country.
/// </summary>
public class UpdateCountryRequest
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Request to add a city to a country.
/// </summary>
public class AddCityRequest
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Request to set or update a shipping rate.
/// </summary>
public class SetShippingRateRequest
{
    public Currency Currency { get; set; }
    public decimal Cost { get; set; }
}
