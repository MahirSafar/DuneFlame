using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/newsletter")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminNewsletterController(
    INewsletterService newsletterService,
    IValidator<BulkEmailRequest> bulkEmailValidator) : ControllerBase
{
    private readonly INewsletterService _newsletterService = newsletterService;
    private readonly IValidator<BulkEmailRequest> _bulkEmailValidator = bulkEmailValidator;

    /// <summary>
    /// Get all newsletter subscribers with pagination and optional search
    /// </summary>
    [HttpGet("subscribers")]
    public async Task<IActionResult> GetAllSubscribers(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        try
        {
            if (pageNumber < 1 || pageSize < 1 || pageSize > 100)
            {
                return BadRequest(new { message = "Invalid pagination parameters. PageNumber must be >= 1, PageSize must be between 1 and 100." });
            }

            var result = await _newsletterService.GetAllSubscribersAsync(pageNumber, pageSize, search);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Send bulk email to all verified newsletter subscribers
    /// </summary>
    [HttpPost("send-bulk")]
    public async Task<IActionResult> SendBulkNewsletter([FromBody] BulkEmailRequest request)
    {
        var validationResult = await _bulkEmailValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        try
        {
            await _newsletterService.SendToAllAsync(request);
            return Ok(new { message = "Bulk newsletter sent successfully. Check logs for details." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to send bulk newsletter.", error = ex.Message });
        }
    }
}
