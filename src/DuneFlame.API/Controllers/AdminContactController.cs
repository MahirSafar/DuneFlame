using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/contacts")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminContactController(IContactService contactService) : ControllerBase
{
    private readonly IContactService _contactService = contactService;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? search = null, [FromQuery] bool? isRead = null)
    {
        var result = await _contactService.GetAllAdminAsync(pageNumber, pageSize, search, isRead);
        return Ok(result);
    }

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        await _contactService.MarkAsReadAsync(id);
        return Ok(new { message = "Contact message marked as read." });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _contactService.DeleteAsync(id);
        return Ok(new { message = "Contact message deleted successfully." });
    }
}
