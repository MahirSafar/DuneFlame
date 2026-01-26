using DuneFlame.Application.DTOs.Basket;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/basket")]
[ApiController]
[Authorize]
public class BasketController(IBasketService basketService, ICurrencyProvider currencyProvider) : ControllerBase
{
    private readonly IBasketService _basketService = basketService;
    private readonly ICurrencyProvider _currencyProvider = currencyProvider;

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
            // Ensure currency is set from request header
            basket.CurrencyCode = _currencyProvider.GetCurrentCurrencyCode();
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
            // Set currency from request header
            basket.CurrencyCode = _currencyProvider.GetCurrentCurrencyCode();

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

    [HttpDelete("{itemId:guid}")]
    public async Task<IActionResult> DeleteProductFromBasket(Guid itemId)
    {
        try
        {
            var userId = GetUserId();
            await _basketService.RemoveItemFromBasketAsync(userId, itemId);
            return Ok(new { message = "Basket item removed successfully" });
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
