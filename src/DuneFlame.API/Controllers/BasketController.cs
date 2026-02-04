using DuneFlame.Application.DTOs.Basket;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/basket")]
[ApiController]
public class BasketController(IBasketService basketService, ICurrencyProvider currencyProvider) : ControllerBase
{
    private readonly IBasketService _basketService = basketService;
    private readonly ICurrencyProvider _currencyProvider = currencyProvider;

    /// <summary>
    /// Gets the authenticated user's ID from claims, or returns null for guest users.
    /// </summary>
    private string? GetUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(userIdClaim) ? null : userIdClaim;
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBasket(string id)
    {
        try
        {
            // For authenticated users: Optionally allow them to fetch using their ID or a provided ID
            var authenticatedUserId = GetUserId();
            var basketId = authenticatedUserId ?? id;

            if (string.IsNullOrWhiteSpace(basketId))
            {
                return BadRequest(new { message = "Basket ID is required" });
            }

            var basket = await _basketService.GetBasketAsync(basketId);
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
    [AllowAnonymous]
    public async Task<IActionResult> UpdateBasket([FromBody] CustomerBasketDto basket)
    {
        try
        {
            if (basket == null || string.IsNullOrWhiteSpace(basket.Id))
            {
                return BadRequest(new { message = "Basket ID is required" });
            }

            // For authenticated users: Force basket.Id to their UserId to ensure persistence
            var authenticatedUserId = GetUserId();
            if (!string.IsNullOrEmpty(authenticatedUserId))
            {
                // User is logged in - tie basket to their account
                basket.Id = authenticatedUserId;
            }
            // For guest users: Use the provided ID from frontend (guest_xyz format)

            // Set currency from request header
            basket.CurrencyCode = _currencyProvider.GetCurrentCurrencyCode();

            await _basketService.UpdateBasketAsync(basket);
            return Ok(new { message = "Basket updated successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}/items/{itemId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> DeleteProductFromBasket(string id, Guid itemId)
    {
        try
        {
            // For authenticated users: Use their UserId instead of provided ID
            var authenticatedUserId = GetUserId();
            var basketId = authenticatedUserId ?? id;

            if (string.IsNullOrWhiteSpace(basketId))
            {
                return BadRequest(new { message = "Basket ID is required" });
            }

            await _basketService.RemoveItemFromBasketAsync(basketId, itemId);
            return Ok(new { message = "Basket item removed successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> DeleteBasket(string id)
    {
        try
        {
            // For authenticated users: Use their UserId instead of provided ID
            var authenticatedUserId = GetUserId();
            var basketId = authenticatedUserId ?? id;

            if (string.IsNullOrWhiteSpace(basketId))
            {
                return BadRequest(new { message = "Basket ID is required" });
            }

            await _basketService.DeleteBasketAsync(basketId);
            return Ok(new { message = "Basket deleted successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
