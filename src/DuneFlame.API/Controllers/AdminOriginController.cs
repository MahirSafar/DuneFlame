using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/origins")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminOriginController(IOriginService originService) : ControllerBase
{
    private readonly IOriginService _originService = originService;

    [HttpPost]
    public async Task<IActionResult> CreateOrigin([FromBody] CreateOriginRequest request)
    {
        try
        {
            var originId = await _originService.CreateAsync(request);
            return CreatedAtAction(nameof(GetOriginById), new { id = originId }, new { id = originId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}", Name = "GetOriginById")]
    [AllowAnonymous]
    public async Task<IActionResult> GetOriginById(Guid id)
    {
        try
        {
            var origin = await _originService.GetByIdAsync(id);
            return Ok(origin);
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

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAllOrigins([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            var origins = await _originService.GetAllAsync(pageNumber, pageSize);
            return Ok(origins);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateOrigin(Guid id, [FromBody] CreateOriginRequest request)
    {
        try
        {
            await _originService.UpdateAsync(id, request);
            return Ok(new { message = "Origin updated successfully." });
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteOrigin(Guid id)
    {
        try
        {
            await _originService.DeleteAsync(id);
            return Ok(new { message = "Origin deleted successfully." });
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
