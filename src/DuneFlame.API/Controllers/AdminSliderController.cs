using DuneFlame.Application.DTOs.Admin.Slider;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/sliders")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminSliderController(ISliderService sliderService) : ControllerBase
{
    private readonly ISliderService _sliderService = sliderService;

    /// <summary>
    /// Create a new slider with image and translations
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateSlider([FromForm] CreateSliderRequest request)
    {
        try
        {
            var sliderId = await _sliderService.CreateAsync(request);
            return CreatedAtAction(nameof(GetSliderById), new { id = sliderId }, new { id = sliderId });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { message = "An error occurred while creating the slider" });
        }
    }

    /// <summary>
    /// Get slider by ID with all translations
    /// </summary>
    [HttpGet("{id:guid}", Name = "GetSliderById")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSliderById(Guid id)
    {
        try
        {
            var slider = await _sliderService.GetByIdAsync(id);
            return Ok(slider);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { message = "An error occurred while retrieving the slider" });
        }
    }

    /// <summary>
    /// Get all sliders with pagination
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAllSliders(
        [FromQuery] int pageNumber = 1, 
        [FromQuery] int pageSize = 10)
    {
        try
        {
            var result = await _sliderService.GetAllAsync(pageNumber, pageSize);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { message = "An error occurred while retrieving sliders" });
        }
    }

    /// <summary>
    /// Update an existing slider
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateSlider(Guid id, [FromForm] UpdateSliderRequest request)
    {
        try
        {
            await _sliderService.UpdateAsync(id, request);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { message = "An error occurred while updating the slider" });
        }
    }

    /// <summary>
    /// Delete a slider
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSlider(Guid id)
    {
        try
        {
            await _sliderService.DeleteAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { message = "An error occurred while deleting the slider" });
        }
    }
}
