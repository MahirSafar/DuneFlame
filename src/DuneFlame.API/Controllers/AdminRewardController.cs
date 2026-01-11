using DuneFlame.Application.DTOs.Reward;
using DuneFlame.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/rewards")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminRewardController(
    IRewardService rewardService,
    IValidator<AdminAdjustRewardRequest> adjustRewardValidator) : ControllerBase
{
    private readonly IRewardService _rewardService = rewardService;
    private readonly IValidator<AdminAdjustRewardRequest> _adjustRewardValidator = adjustRewardValidator;

    [HttpPost("adjust")]
    public async Task<IActionResult> AdjustRewardPoints([FromBody] AdminAdjustRewardRequest request)
    {
        var validationResult = await _adjustRewardValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        try
        {
            await _rewardService.AdminAdjustPointsAsync(request.UserId, request.Amount, request.Reason);
            var stats = await _rewardService.GetWalletAsync(request.UserId);
            return Ok(new { message = "Reward points adjusted successfully", stats });
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
