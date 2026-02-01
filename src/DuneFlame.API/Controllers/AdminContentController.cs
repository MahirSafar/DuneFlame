using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/content")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminContentController(
    IAdminContentService adminContentService,
    IValidator<CreateAboutSectionRequest> aboutSectionValidator) : ControllerBase
{
    private readonly IAdminContentService _adminContentService = adminContentService;
    private readonly IValidator<CreateAboutSectionRequest> _aboutSectionValidator = aboutSectionValidator;

    // About Section endpoints
    [HttpGet("about-sections")]
    public async Task<IActionResult> GetAllAboutSections()
    {
        try
        {
            var sections = await _adminContentService.GetAllAboutSectionsAsync();
            return Ok(sections);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("about-sections/{id:guid}")]
    public async Task<IActionResult> GetAboutSectionById(Guid id)
    {
        try
        {
            var section = await _adminContentService.GetAboutSectionByIdAsync(id);
            return Ok(section);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("about-sections")]
    public async Task<IActionResult> CreateAboutSection([FromBody] CreateAboutSectionRequest request)
    {
        var validationResult = await _aboutSectionValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        try
        {
            var sectionId = await _adminContentService.CreateAboutSectionAsync(request);
            return CreatedAtAction(nameof(GetAboutSectionById), new { id = sectionId }, new { id = sectionId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("about-sections/{id:guid}")]
    public async Task<IActionResult> UpdateAboutSection(Guid id, [FromBody] CreateAboutSectionRequest request)
    {
        var validationResult = await _aboutSectionValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        try
        {
            await _adminContentService.UpdateAboutSectionAsync(id, request);
            return Ok(new { message = "About section updated successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("about-sections/{id:guid}")]
    public async Task<IActionResult> DeleteAboutSection(Guid id)
    {
        try
        {
            await _adminContentService.DeleteAboutSectionAsync(id);
            return Ok(new { message = "About section deleted successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
