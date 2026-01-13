using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/dashboard")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminDashboardController(IAdminDashboardService adminDashboardService) : ControllerBase
{
    private readonly IAdminDashboardService _adminDashboardService = adminDashboardService;

    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetDashboardStats()
    {
        try
        {
            var stats = await _adminDashboardService.GetDashboardStatsAsync();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
