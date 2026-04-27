using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/public/sliders")]
[ApiController]
public class PublicSliderController(ISliderService sliderService) : ControllerBase
{
    private readonly ISliderService _sliderService = sliderService;

    /// <summary>
    /// Get all active sliders for the storefront, pre-translated based on Accept-Language header.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPublicSliders()
    {
        try
        {
            var acceptLanguage = Request.Headers.AcceptLanguage.FirstOrDefault() ?? "en";
            var languageCode = acceptLanguage.Split(',')[0].Trim().Split('-')[0];

            var sliders = await _sliderService.GetPublicSlidersAsync(languageCode);
            return Ok(sliders);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while retrieving sliders" });
        }
    }
}
