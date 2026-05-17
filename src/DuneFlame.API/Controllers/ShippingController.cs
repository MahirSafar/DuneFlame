using DuneFlame.Application.DTOs.Shipping;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Public API controller for shipping-related operations.
/// Country/City names are resolved dynamically from translations based on the Accept-Language header.
/// </summary>
[Route("api/v1/shipping")]
[ApiController]
public class ShippingController(IShippingService shippingService) : ControllerBase
{
    private readonly IShippingService _shippingService = shippingService;

    // --------------- helpers ---------------

    /// <summary>Extracts the 2-char language code from Accept-Language header (defaults to "en").</summary>
    private string ExtractLanguage()
    {
        try
        {
            var header = Request.Headers["Accept-Language"].ToString();
            if (string.IsNullOrWhiteSpace(header)) return "en";
            var primary = header.Split(',')[0].Trim();
            var lang = primary.Length >= 2 ? primary[..2].ToLower() : "en";
            return lang == "ar" ? "ar" : "en";
        }
        catch { return "en"; }
    }

    /// <summary>Resolves the display name for a Country from its Translations collection.</summary>
    private static string ResolveCountryName(
        DuneFlame.Domain.Entities.Country country, string lang)
        => country.Translations?.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
           ?? country.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
           ?? country.Name;

    /// <summary>Resolves the display name for a City from its Translations collection.</summary>
    private static string ResolveCityName(
        DuneFlame.Domain.Entities.City city, string lang)
        => city.Translations?.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
           ?? city.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
           ?? city.Name;

    // --------------- endpoints ---------------

    [HttpGet("countries")]
    [ProducesResponseType(typeof(List<CountryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCountries()
    {
        try
        {
            var lang = ExtractLanguage();
            var countries = await _shippingService.GetAllCountriesAsync(includeInactive: false);

            var countryDtos = countries.Select(c => new CountryDto
            {
                Id = c.Id,
                Name = ResolveCountryName(c, lang),
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

    [HttpGet("countries/{countryId:guid}/cities")]
    [ProducesResponseType(typeof(List<CityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCitiesByCountry(Guid countryId)
    {
        try
        {
            var lang = ExtractLanguage();
            var country = await _shippingService.GetCountryByIdAsync(countryId);

            if (country == null)
                return NotFound(new { message = $"Country with ID '{countryId}' not found." });

            var cityDtos = country.Cities
                .Select(c => new CityDto
                {
                    Id = c.Id,
                    Name = ResolveCityName(c, lang),
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

    [HttpGet("countries/code/{code}")]
    [ProducesResponseType(typeof(CountryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCountryByCode(string code)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length != 2)
                return BadRequest(new { message = "Country code must be exactly 2 characters (ISO 3166-1 alpha-2)." });

            var lang = ExtractLanguage();
            var country = await _shippingService.GetCountryByCodeAsync(code);

            if (country == null)
                return NotFound(new { message = $"Country with code '{code}' not found." });

            var countryDto = new CountryDto
            {
                Id = country.Id,
                Name = ResolveCountryName(country, lang),
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

    [HttpPost("calculate")]
    [ProducesResponseType(typeof(CalculateShippingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CalculateShipping([FromBody] CalculateShippingRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CountryCode) || string.IsNullOrWhiteSpace(request.Currency))
                return BadRequest(new { message = "CountryCode and Currency are required." });

            if (request.Subtotal < 0)
                return BadRequest(new { message = "Subtotal cannot be negative." });

            string normalizedCountryCode = request.CountryCode.Length > 2
                ? request.CountryCode[..2]
                : request.CountryCode;

            if (!Enum.TryParse<DuneFlame.Domain.Enums.Currency>(request.Currency, ignoreCase: true, out var currency))
                return BadRequest(new { message = "Invalid currency. Supported currencies: USD, AED." });

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

            var shippingPrice = await _shippingService.GetShippingCostWithPromotionAsync(
                normalizedCountryCode, currency, request.Subtotal);

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
