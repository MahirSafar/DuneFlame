using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DuneFlame.API.Controllers;

[Route("api/v1/newsletter")]
[ApiController]
public class NewsletterController(INewsletterService newsletterService) : ControllerBase
{
    private readonly INewsletterService _newsletterService = newsletterService;

    [HttpPost("subscribe")]
    [EnableRateLimiting("AuthPolicy")] // Rate Limit tətbiq edirik
    public async Task<IActionResult> Subscribe([FromBody] NewsletterRequest request)
    {
        await _newsletterService.SubscribeAsync(request);
        return Ok(new { message = "Verification email sent. Please check your inbox." });
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify([FromQuery] string token)
    {
        var result = await _newsletterService.VerifyEmailAsync(token);
        if (result) return Ok("Subscription verified!");
        return BadRequest("Invalid token.");
    }

    [HttpGet("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromQuery] string token)
    {
        await _newsletterService.UnsubscribeAsync(token);
        return Ok("Unsubscribed successfully.");
    }
}