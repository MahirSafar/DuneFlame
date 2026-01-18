using DuneFlame.Application.DTOs.Basket;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/basket")]
[ApiController]
[Authorize]
public class BasketController(IBasketService basketService) : ControllerBase
{
    private readonly IBasketService _basketService = basketService;

    private string GetUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim))
        {
            throw new UnauthorizedAccessException("User ID not found in claims");
        }
        return userIdClaim;
    }

    [HttpGet]
    public async Task<IActionResult> GetBasket()
    {
        try
        {
            var userId = GetUserId();
            var basket = await _basketService.GetBasketAsync(userId);
            return Ok(basket);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateBasket([FromBody] CustomerBasketDto basket)
    {
        try
        {
            var userId = GetUserId();
            // Force basket.Id to match the authenticated user's ID to prevent cross-user manipulation
            basket.Id = userId;
            
            await _basketService.UpdateBasketAsync(basket);
            return Ok(new { message = "Basket updated successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteBasket()
    {
        try
        {
            var userId = GetUserId();
            await _basketService.DeleteBasketAsync(userId);
            return Ok(new { message = "Basket deleted successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
