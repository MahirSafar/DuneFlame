using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/rewards")]
[ApiController]
[Authorize]
public class RewardController(IRewardService rewardService) : ControllerBase
{
    private readonly IRewardService _rewardService = rewardService;

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in claims");
        }
        return userId;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyRewardWallet()
    {
        try
        {
            var userId = GetUserId();
            var stats = await _rewardService.GetWalletAsync(userId);
            var transactions = await _rewardService.GetTransactionsAsync(userId);

            return Ok(new { stats, transactions });
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
