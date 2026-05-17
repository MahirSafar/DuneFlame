using DuneFlame.Application.DTOs.Wholesale;

namespace DuneFlame.API.Controllers;

[ApiController]
public class WholesaleController(IWholesaleService wholesaleService) : ControllerBase
{
    private readonly IWholesaleService _wholesaleService = wholesaleService;

    [HttpPost("api/v1/wholesale")]
    [EnableRateLimiting("ContactPolicy")]
    public async Task<IActionResult> Submit([FromBody] CreateWholesaleLeadRequest request)
    {
        await _wholesaleService.SubmitLeadAsync(request);
        return Ok(new { message = "Your wholesale inquiry has been received. We will contact you shortly." });
    }

    [HttpGet("api/v1/admin/wholesale")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _wholesaleService.GetAllAdminAsync(pageNumber, pageSize);
        return Ok(result);
    }

    [HttpPatch("api/v1/admin/wholesale/{id:guid}/reviewed")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> MarkAsReviewed(Guid id)
    {
        await _wholesaleService.MarkAsReviewedAsync(id);
        return Ok(new { message = "Wholesale lead marked as reviewed." });
    }
}
