using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DuneFlame.API.Controllers;

[Route("api/v1/contact")]
[ApiController]
public class ContactController(IContactService contactService) : ControllerBase
{
    private readonly IContactService _contactService = contactService;

    [HttpPost]
    [EnableRateLimiting("AuthPolicy")] // Spam qorunması
    public async Task<IActionResult> SubmitMessage([FromBody] ContactMessageRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _contactService.SubmitMessageAsync(request, ip);
        return Ok(new { message = "Message received. We will contact you shortly." });
    }
}